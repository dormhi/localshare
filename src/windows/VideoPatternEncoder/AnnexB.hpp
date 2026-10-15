// SPDX-License-Identifier: Apache-2.0
// Portable H.264 Annex-B helpers for the encoder helper; no Windows dependency.
#pragma once
#include <cstddef>
#include <cstdint>
#include <vector>
namespace localshare::video {
struct Nal { const std::uint8_t* data; std::size_t size; };

// Splits an Annex-B byte stream on 3- or 4-byte start codes. Returns false when
// the stream does not begin with a start code or contains an empty NAL unit.
inline bool split_nals(const std::uint8_t* data, std::size_t size, std::vector<Nal>& out) {
    out.clear();
    if (!data) return false;
    auto start_code = [&](std::size_t at) -> std::size_t {
        if (at + 3 <= size && data[at] == 0 && data[at+1] == 0 && data[at+2] == 1) return 3;
        if (at + 4 <= size && data[at] == 0 && data[at+1] == 0 && data[at+2] == 0 && data[at+3] == 1) return 4;
        return 0;
    };
    std::size_t prefix = start_code(0);
    if (prefix == 0) return false;
    std::size_t begin = prefix;
    for (std::size_t i = begin; i < size; ++i) {
        const std::size_t next = start_code(i);
        if (next == 0) continue;
        if (i == begin) return false;
        out.push_back({data + begin, i - begin});
        begin = i + next;
        i = begin - 1;
    }
    if (begin >= size) return false;
    out.push_back({data + begin, size - begin});
    return true;
}
inline unsigned nal_type(const Nal& nal) noexcept { return nal.data[0] & 31U; }

struct FilteredAccessUnit { std::vector<std::uint8_t> bytes; bool idr = false; bool slice = false; };

// Rewrites an access unit with 4-byte start codes and drops NAL types the
// tablet does not need: SEI (6), access unit delimiter (9), filler (12).
// Every other type is kept so the Android validator still rejects surprises.
inline bool filter_access_unit(const std::uint8_t* data, std::size_t size, FilteredAccessUnit& out) {
    std::vector<Nal> nals;
    if (!split_nals(data, size, nals)) return false;
    out = {};
    out.bytes.reserve(size);
    for (const auto& nal : nals) {
        const unsigned type = nal_type(nal);
        if (type == 6 || type == 9 || type == 12) continue;
        if (type == 5) out.idr = true;
        if (type == 1 || type == 5) out.slice = true;
        out.bytes.insert(out.bytes.end(), {0, 0, 0, 1});
        out.bytes.insert(out.bytes.end(), nal.data, nal.data + nal.size);
    }
    return out.slice;
}

// Builds the CONFIG payload (SPS then PPS, 4-byte start codes) from an access
// unit when the encoder did not publish a sequence header before streaming.
inline bool extract_config(const std::uint8_t* data, std::size_t size, std::vector<std::uint8_t>& out) {
    std::vector<Nal> nals;
    if (!split_nals(data, size, nals)) return false;
    const Nal* sps = nullptr; const Nal* pps = nullptr;
    for (const auto& nal : nals) {
        if (nal_type(nal) == 7 && !sps) sps = &nal;
        if (nal_type(nal) == 8 && !pps) pps = &nal;
    }
    if (!sps || !pps) return false;
    out.clear();
    for (const Nal* nal : {sps, pps}) {
        out.insert(out.end(), {0, 0, 0, 1});
        out.insert(out.end(), nal->data, nal->data + nal->size);
    }
    return true;
}
}
