// SPDX-License-Identifier: Apache-2.0
#include "../../src/windows/VideoPatternEncoder/Pattern.hpp"
#include <vector>
#include <iostream>
#include <cstdlib>
using namespace localshare::video;
#define CHECK(x) do { if (!(x)) std::exit(1); } while (false)
int main() {
    std::vector<std::uint8_t> pixels(RawBytes + 2, 99);
    CHECK(!fill_pattern(nullptr,RawBytes,0));
    CHECK(!fill_pattern(pixels.data()+1,RawBytes-1,0));
    CHECK(fill_pattern(pixels.data()+1,RawBytes,0));
    CHECK(pixels.front()==99 && pixels.back()==99);
    CHECK(pixels[1]==235 && pixels[1+Width*Height]==128);
    for (std::size_t i = Width*Height+1; i <= RawBytes; ++i) CHECK(pixels[i]==128);
    const auto initial=pixels;
    CHECK(fill_pattern(pixels.data()+1,RawBytes,Width)); CHECK(pixels==initial);
    CHECK(fill_pattern(pixels.data()+1,RawBytes,77)); CHECK(pixels!=initial);
    for (const auto fps : {1U,15U,30U,60U,120U,144U,240U}) CHECK(valid_fps(fps));
    CHECK(!valid_fps(0) && !valid_fps(MaxFps+1));
    std::cout << "PASS: deterministic NV12 pattern, buffer guards and non-30/60 FPS limits\n";
}
