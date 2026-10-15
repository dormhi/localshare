// SPDX-License-Identifier: Apache-2.0
#include "../../src/windows/VideoPatternEncoder/AnnexB.hpp"
#include <cstdlib>
#include <iostream>
#include <vector>
using namespace localshare::video;
#define CHECK(x) do { if (!(x)) { std::cerr << "FAIL line " << __LINE__ << "\n"; std::exit(1); } } while (false)
int main() {
    // AUD(9) + SPS(7) + PPS(8) + SEI(6) + IDR(5), mixed 3/4-byte start codes.
    const std::vector<std::uint8_t> unit = {0,0,0,1,0x09,0xF0, 0,0,1,0x67,0x42,0xC0,0x1F, 0,0,0,1,0x68,0xCE,
                                            0,0,1,0x06,0x05,0x01, 0,0,0,1,0x65,0x88,0x84};
    std::vector<Nal> nals;
    CHECK(split_nals(unit.data(), unit.size(), nals) && nals.size() == 5);
    CHECK(nal_type(nals[0]) == 9 && nal_type(nals[1]) == 7 && nal_type(nals[4]) == 5);
    FilteredAccessUnit filtered;
    CHECK(filter_access_unit(unit.data(), unit.size(), filtered) && filtered.idr && filtered.slice);
    const std::vector<std::uint8_t> expected = {0,0,0,1,0x67,0x42,0xC0,0x1F, 0,0,0,1,0x68,0xCE, 0,0,0,1,0x65,0x88,0x84};
    CHECK(filtered.bytes == expected);
    std::vector<std::uint8_t> config;
    CHECK(extract_config(unit.data(), unit.size(), config));
    CHECK(config == std::vector<std::uint8_t>(expected.begin(), expected.begin() + 14));

    const std::vector<std::uint8_t> p_frame = {0,0,0,1,0x41,0x9A};
    CHECK(filter_access_unit(p_frame.data(), p_frame.size(), filtered) && !filtered.idr);
    CHECK(!extract_config(p_frame.data(), p_frame.size(), config));
    // Malformed: no start code, empty NAL, trailing start code, null, only SEI.
    const std::vector<std::uint8_t> none = {0x65,0x88}, empty = {0,0,1,0,0,1,0x65}, trailing = {0,0,1,0x65,0,0,1};
    const std::vector<std::uint8_t> sei_only = {0,0,1,0x06,0x01};
    CHECK(!split_nals(none.data(), none.size(), nals));
    CHECK(!split_nals(empty.data(), empty.size(), nals));
    CHECK(!split_nals(trailing.data(), trailing.size(), nals));
    CHECK(!split_nals(nullptr, 4, nals));
    CHECK(!filter_access_unit(sei_only.data(), sei_only.size(), filtered));
    std::cout << "PASS: Annex-B split, AUD/SEI/filler filtering, IDR detection and SPS/PPS config extraction\n";
}
