// SPDX-License-Identifier: Apache-2.0
#pragma once
#include <cstddef>
#include <cstdint>
namespace localshare::video {
inline constexpr std::uint32_t Width = 1280, Height = 800;
inline constexpr std::size_t RawBytes = std::size_t{Width} * Height * 3 / 2;
inline constexpr std::uint32_t MaxFrame = 1048576, MaxConfig = 65536, MaxFps = 1000;
inline bool fill_pattern(std::uint8_t* output, std::size_t size, std::uint64_t frame) noexcept {
    if (!output || size != RawBytes) return false;
    constexpr std::uint8_t shades[] = {235,210,170,145,106,81,41,16};
    const auto marker = static_cast<std::uint32_t>(frame % Width);
    for (std::uint32_t y = 0; y < Height; ++y)
        for (std::uint32_t x = 0; x < Width; ++x)
            output[std::size_t{y} * Width + x] = (x >= marker && x - marker < 16) ? 235 :
                (y % 64 == 0 ? 32 : shades[x / (Width / 8)]);
    for (std::size_t i = std::size_t{Width} * Height; i < size; ++i) output[i] = 128;
    return true;
}
inline bool valid_fps(std::uint32_t value) noexcept { return value >= 1 && value <= MaxFps; }
}
