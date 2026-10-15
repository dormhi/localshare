// SPDX-License-Identifier: Apache-2.0
// Windows user-mode proof helper; no IddCx/desktop capture, no network access.
#define NOMINMAX
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mftransform.h>
#include <mferror.h>
#include <codecapi.h>
#include <wmcodecdsp.h>
#include <wrl/client.h>
#include <array>
#include <charconv>
#include <chrono>
#include <cstdio>
#include <stdexcept>
#include <string>
#include <vector>
#include "AnnexB.hpp"
#include "Pattern.hpp"
using Microsoft::WRL::ComPtr;
using namespace localshare::video;

namespace {
struct Failure { HRESULT result; };
void check(HRESULT result) { if (FAILED(result)) throw Failure{result}; }
struct Runtime {
    Runtime() { check(CoInitializeEx(nullptr, COINIT_MULTITHREADED));
        const auto hr = MFStartup(MF_VERSION); if (FAILED(hr)) { CoUninitialize(); check(hr); } }
    ~Runtime() { MFShutdown(); CoUninitialize(); }
};
struct Timer {
    HANDLE value = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    Timer() { if (!value) throw Failure{HRESULT_FROM_WIN32(GetLastError())}; }
    ~Timer() { CloseHandle(value); }
    void wait(std::int64_t ticks) {
        if (ticks <= 0) return;
        LARGE_INTEGER due{}; due.QuadPart = -ticks;
        if (!SetWaitableTimer(value, &due, 0, nullptr, nullptr, FALSE) || WaitForSingleObject(value, INFINITE) != WAIT_OBJECT_0)
            throw Failure{E_FAIL};
    }
};
void write_all(const void* data, std::size_t size) {
    const auto* bytes = static_cast<const std::uint8_t*>(data);
    while (size != 0) {
        DWORD written = 0;
        if (!WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), bytes, static_cast<DWORD>(size), &written, nullptr) || written == 0)
            throw Failure{HRESULT_FROM_WIN32(ERROR_BROKEN_PIPE)};
        size -= written; bytes += written;
    }
}
void packet(std::uint8_t type, const BYTE* bytes, std::uint32_t size, std::uint32_t seq,
            std::uint64_t pts = 0, bool key = false) {
    if ((type == 4 && (size == 0 || size > MaxConfig)) || (type == 5 && (size == 0 || size > MaxFrame)))
        throw Failure{E_INVALIDARG};
    std::array<std::uint8_t,24> header{{'T','V','I','D',1,type,0,static_cast<std::uint8_t>(key)}};
    for (unsigned i = 0; i < 4; ++i) {
        header[8+i] = static_cast<std::uint8_t>(size >> (24-8*i));
        header[12+i] = static_cast<std::uint8_t>(seq >> (24-8*i));
    }
    for (unsigned i = 0; i < 8; ++i) header[16+i] = static_cast<std::uint8_t>(pts >> (56-8*i));
    write_all(header.data(), header.size()); write_all(bytes, size);
}
void await_credit() {
    char byte = 0; DWORD count = 0;
    if (!ReadFile(GetStdHandle(STD_INPUT_HANDLE), &byte, 1, &count, nullptr) || count != 1 || byte != 'A')
        throw Failure{HRESULT_FROM_WIN32(ERROR_BROKEN_PIPE)};
}
void attribute(IMFMediaType* type, REFGUID subtype, std::uint32_t fps) {
    check(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
    check(type->SetGUID(MF_MT_SUBTYPE, subtype));
    check(type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
    check(MFSetAttributeSize(type, MF_MT_FRAME_SIZE, Width, Height));
    check(MFSetAttributeRatio(type, MF_MT_FRAME_RATE, fps, 1));
    check(MFSetAttributeRatio(type, MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
}
std::uint32_t number(const char* text) {
    std::uint32_t value = 0;
    const auto end = text + std::char_traits<char>::length(text);
    const auto parsed = std::from_chars(text, end, value);
    if (parsed.ec != std::errc{} || parsed.ptr != end) throw Failure{E_INVALIDARG};
    return value;
}
enum class Mode { Auto, Hardware, Software };
struct Encoder {
    ComPtr<IMFTransform> transform;
    ComPtr<IMFActivate> activation;      // hardware MFTs are shut down through their activation object
    ComPtr<IMFMediaEventGenerator> events; // set only for asynchronous (hardware) MFTs
    bool hardware = false;
    std::wstring name;
    ~Encoder() { transform.Reset(); if (activation) activation->ShutdownObject(); }
};
// Required codec settings fail the software encoder. Hardware vendors differ in
// which properties they expose, so there they are best effort.
void codec_value(ICodecAPI* api, const GUID& property, VARIANT value, bool required) {
    const HRESULT hr = api->SetValue(&property, &value);
    if (required) check(hr);
}
void configure(Encoder& encoder, std::uint32_t fps, std::uint32_t bitrate) {
    ComPtr<ICodecAPI> settings;
    const HRESULT has_api = encoder.transform.As(&settings);
    if (!encoder.hardware) check(has_api);
    if (settings) {
        const bool required = !encoder.hardware;
        VARIANT low{}; low.vt = VT_BOOL; low.boolVal = VARIANT_TRUE;
        codec_value(settings.Get(), CODECAPI_AVLowLatencyMode, low, required);
        VARIANT rate{}; rate.vt = VT_UI4; rate.ulVal = eAVEncCommonRateControlMode_CBR;
        codec_value(settings.Get(), CODECAPI_AVEncCommonRateControlMode, rate, required);
        VARIANT gop{}; gop.vt = VT_UI4; gop.ulVal = fps * 2;
        codec_value(settings.Get(), CODECAPI_AVEncMPVGOPSize, gop, required);
        VARIANT no_b{}; no_b.vt = VT_UI4; no_b.ulVal = 0;
        codec_value(settings.Get(), CODECAPI_AVEncMPVDefaultBPictureCount, no_b, false);
    }
    ComPtr<IMFMediaType> output; check(MFCreateMediaType(&output));
    attribute(output.Get(), MFVideoFormat_H264, fps);
    check(output->SetUINT32(MF_MT_AVG_BITRATE, bitrate));
    check(output->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Base));
    // Let the OS choose the required AVC level, including higher requested FPS.
    check(encoder.transform->SetOutputType(0, output.Get(), 0));
    ComPtr<IMFMediaType> input; check(MFCreateMediaType(&input));
    attribute(input.Get(), MFVideoFormat_NV12, fps);
    check(input->SetUINT32(MF_MT_DEFAULT_STRIDE, Width));
    check(encoder.transform->SetInputType(0, input.Get(), 0));
}
// Hardware first: a GPU encoder keeps the host CPU free, which is a product
// requirement. Each candidate must accept the exact Baseline/NV12 format.
bool open_hardware(Encoder& encoder, std::uint32_t fps, std::uint32_t bitrate) {
    MFT_REGISTER_TYPE_INFO in{MFMediaType_Video, MFVideoFormat_NV12}, out{MFMediaType_Video, MFVideoFormat_H264};
    IMFActivate** found = nullptr; UINT32 count = 0;
    if (FAILED(MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER,
                         &in, &out, &found, &count))) return false;
    std::vector<ComPtr<IMFActivate>> candidates;
    for (UINT32 i = 0; i < count; ++i) candidates.emplace_back().Attach(found[i]);
    CoTaskMemFree(found);
    for (auto& candidate : candidates) {
        Encoder attempt; attempt.hardware = true; attempt.activation = candidate;
        if (FAILED(candidate->ActivateObject(IID_PPV_ARGS(&attempt.transform)))) { attempt.activation.Reset(); continue; }
        try {
            ComPtr<IMFAttributes> attributes; check(attempt.transform->GetAttributes(&attributes));
            UINT32 async = 0; (void)attributes->GetUINT32(MF_TRANSFORM_ASYNC, &async);
            if (async) {
                check(attributes->SetUINT32(MF_TRANSFORM_ASYNC_UNLOCK, TRUE));
                check(attempt.transform.As(&attempt.events));
            }
            WCHAR* text = nullptr; UINT32 length = 0;
            if (SUCCEEDED(candidate->GetAllocatedString(MFT_FRIENDLY_NAME_Attribute, &text, &length))) {
                attempt.name.assign(text, length); CoTaskMemFree(text);
            }
            configure(attempt, fps, bitrate);
        } catch (const Failure&) { continue; } // try the next adapter's encoder
        encoder.transform = std::move(attempt.transform);
        encoder.activation = std::move(attempt.activation);
        encoder.events = std::move(attempt.events);
        encoder.hardware = true; encoder.name = std::move(attempt.name);
        return true;
    }
    return false;
}
void open_software(Encoder& encoder, std::uint32_t fps, std::uint32_t bitrate) {
    check(CoCreateInstance(CLSID_CMSH264EncoderMFT, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&encoder.transform)));
    encoder.hardware = false; encoder.name = L"Microsoft H.264 software encoder";
    configure(encoder, fps, bitrate);
}
class Stream {
public:
    Stream(Encoder& encoder, std::uint32_t fps) : encoder_(encoder), fps_(fps), begin_(std::chrono::steady_clock::now()), due_(begin_) {
        ComPtr<IMFMediaType> negotiated; check(encoder.transform->GetOutputCurrentType(0, &negotiated));
        UINT32 config_size = 0;
        if (SUCCEEDED(negotiated->GetBlobSize(MF_MT_MPEG_SEQUENCE_HEADER, &config_size)) && config_size != 0) {
            if (config_size > MaxConfig) throw Failure{E_INVALIDARG};
            std::vector<BYTE> config(config_size);
            check(negotiated->GetBlob(MF_MT_MPEG_SEQUENCE_HEADER, config.data(), config_size, nullptr));
            send_config(config);
        } // otherwise many hardware encoders only emit SPS/PPS inline with the first IDR
        check(encoder.transform->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0));
        check(encoder.transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0));
        check(encoder.transform->GetOutputStreamInfo(0, &info_));
        constexpr DWORD ScratchLimit = 4 * 1048576;
        if (info_.cbSize > ScratchLimit) throw Failure{E_OUTOFMEMORY};
    }
    void run() { encoder_.events ? run_async() : run_sync(); }
private:
    void send_config(const std::vector<BYTE>& config) {
        if (config.empty() || config.size() > MaxConfig) throw Failure{E_INVALIDARG};
        packet(4, config.data(), static_cast<std::uint32_t>(config.size()), 2);
        configured_ = true;
        await_credit(); // do not send frames while Android is configuring
    }
    ComPtr<IMFSample> next_input() {
        auto now = std::chrono::steady_clock::now();
        timer_.wait(std::chrono::duration_cast<std::chrono::nanoseconds>(due_ - now).count() / 100);
        now = std::chrono::steady_clock::now();
        due_ = now + std::chrono::nanoseconds(1000000000LL / fps_); // never catch up in a burst
        const auto pts = std::chrono::duration_cast<std::chrono::microseconds>(now - begin_).count();
        ComPtr<IMFMediaBuffer> raw; check(MFCreateMemoryBuffer(static_cast<DWORD>(RawBytes), &raw));
        BYTE* memory = nullptr; check(raw->Lock(&memory, nullptr, nullptr));
        const bool filled = fill_pattern(memory, RawBytes, frame_number_++);
        check(raw->Unlock()); if (!filled) throw Failure{E_FAIL};
        check(raw->SetCurrentLength(static_cast<DWORD>(RawBytes)));
        ComPtr<IMFSample> sample; check(MFCreateSample(&sample)); check(sample->AddBuffer(raw.Get()));
        check(sample->SetSampleTime(pts * 10)); check(sample->SetSampleDuration(10000000LL / fps_));
        return sample;
    }
    // Returns false when the encoder needs more input before it can produce output.
    bool pull_output() {
        const bool provides = (info_.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES) != 0;
        ComPtr<IMFSample> provided;
        MFT_OUTPUT_DATA_BUFFER result{};
        if (!provides) {
            check(MFCreateSample(&provided));
            ComPtr<IMFMediaBuffer> storage;
            check(MFCreateMemoryBuffer(info_.cbSize ? info_.cbSize : MaxFrame, &storage));
            check(provided->AddBuffer(storage.Get())); result.pSample = provided.Get();
        }
        DWORD status = 0;
        const HRESULT hr = encoder_.transform->ProcessOutput(0, 1, &result, &status);
        ComPtr<IMFCollection> events; events.Attach(result.pEvents);
        ComPtr<IMFSample> encoded;
        if (provides) encoded.Attach(result.pSample); else encoded = provided;
        if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT) return false;
        check(hr); // unexpected format changes are a failed proof, not a silent reconfigure
        if (!encoded) throw Failure{E_FAIL};
        emit(encoded.Get());
        return true;
    }
    void emit(IMFSample* encoded) {
        ComPtr<IMFMediaBuffer> data; check(encoded->ConvertToContiguousBuffer(&data));
        DWORD size = 0; check(data->GetCurrentLength(&size));
        if (size == 0 || size > MaxFrame) throw Failure{E_INVALIDARG};
        LONGLONG stamp = 0; check(encoded->GetSampleTime(&stamp));
        if (stamp < 0) throw Failure{E_INVALIDARG};
        UINT32 clean = 0; (void)encoded->GetUINT32(MFSampleExtension_CleanPoint, &clean);
        BYTE* compressed = nullptr; check(data->Lock(&compressed, nullptr, nullptr));
        FilteredAccessUnit unit; std::vector<BYTE> config;
        const bool parsed = filter_access_unit(compressed, size, unit);
        const bool has_config = !configured_ && parsed && extract_config(compressed, size, config);
        check(data->Unlock());
        if (!parsed || unit.bytes.size() > MaxFrame) throw Failure{E_INVALIDARG};
        if (!configured_) {
            if (!has_config || !unit.idr) throw Failure{MF_E_INVALIDMEDIATYPE}; // first output must carry SPS/PPS + IDR
            send_config(config);
        }
        if (sequence_ == UINT32_MAX) throw Failure{E_BOUNDS};
        packet(5, unit.bytes.data(), static_cast<std::uint32_t>(unit.bytes.size()), sequence_++,
               static_cast<std::uint64_t>(stamp / 10), clean != 0 || unit.idr);
        await_credit(); // next output only after the tablet ACK reaches the host
    }
    void run_sync() {
        for (;;) {
            const auto sample = next_input();
            check(encoder_.transform->ProcessInput(0, sample.Get(), 0));
            while (pull_output()) {} // drain before more input: no unbounded raw-frame queue
        }
    }
    // Asynchronous hardware MFTs request input and announce output through
    // events. Their internal pipeline depth is fixed by the driver; this loop
    // never supplies input unless asked and never holds more than one output.
    void run_async() {
        for (;;) {
            ComPtr<IMFMediaEvent> event; check(encoder_.events->GetEvent(0, &event));
            MediaEventType type = MEUnknown; check(event->GetType(&type));
            HRESULT status = S_OK; check(event->GetStatus(&status)); check(status);
            if (type == METransformNeedInput) {
                const auto sample = next_input();
                check(encoder_.transform->ProcessInput(0, sample.Get(), 0));
            } else if (type == METransformHaveOutput) {
                (void)pull_output();
            } else if (type == MEError) {
                throw Failure{E_FAIL};
            }
        }
    }
    Encoder& encoder_;
    const std::uint32_t fps_;
    const std::chrono::steady_clock::time_point begin_;
    std::chrono::steady_clock::time_point due_;
    MFT_OUTPUT_STREAM_INFO info_{};
    Timer timer_;
    bool configured_ = false;
    std::uint32_t sequence_ = 3;
    std::uint64_t frame_number_ = 0;
};
// Streams until the host closes the pipe or an error occurs; ends only by exception.
[[noreturn]] void encode(std::uint32_t fps, std::uint32_t bitrate, Mode mode) {
    Runtime runtime;
    Encoder encoder;
    const bool hardware = mode != Mode::Software && open_hardware(encoder, fps, bitrate);
    if (!hardware) {
        if (mode == Mode::Hardware) throw Failure{MF_E_TOPO_CODEC_NOT_FOUND};
        open_software(encoder, fps, bitrate);
    }
    // stderr carries diagnostics only; stdout is reserved for protocol bytes.
    std::fprintf(stderr, "LocalShare encoder: %s (%ls)\n", encoder.hardware ? "hardware" : "software", encoder.name.c_str());
    std::fflush(stderr);
    Stream stream(encoder, fps);
    stream.run();
    throw Failure{E_UNEXPECTED};
}
Mode mode_argument(const char* text) {
    const std::string value = text;
    if (value == "auto") return Mode::Auto;
    if (value == "hardware") return Mode::Hardware;
    if (value == "software") return Mode::Software;
    throw Failure{E_INVALIDARG};
}
}
int main(int argc, char** argv) {
    try {
        if (argc != 3 && argc != 4) throw Failure{E_INVALIDARG};
        const auto fps = number(argv[1]), bitrate = number(argv[2]);
        if (!valid_fps(fps) || bitrate < 100000 || bitrate > 50000000) throw Failure{E_INVALIDARG};
        encode(fps, bitrate, argc == 4 ? mode_argument(argv[3]) : Mode::Auto);
    } catch (const Failure& error) {
        std::fprintf(stderr, "Pattern encoder failed (HRESULT 0x%08lx).\n", static_cast<unsigned long>(error.result));
        return 1;
    } catch (...) { std::fputs("Pattern encoder failed.\n", stderr); return 1; }
}
