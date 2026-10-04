// SPDX-License-Identifier: Apache-2.0
package com.dormhi.localshare
import java.io.ByteArrayInputStream
import java.io.IOException
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

private fun rejected(action: () -> Unit) {
    try { action() } catch (_: IOException) { return }
    error("Malformed input accepted")
}
private fun makeSps(width: Int, height: Int): ByteArray {
    val bits = StringBuilder()
    fun fixed(value: Int, size: Int) { bits.append(value.toString(2).padStart(size, '0')) }
    fun ue(value: Int) { val code = (value + 1).toString(2); bits.append("0".repeat(code.length - 1)); bits.append(code) }
    fixed(66,8); fixed(0,8); fixed(32,8); ue(0); ue(0); ue(0); ue(0); ue(1); fixed(0,1)
    ue(width/16-1); ue(height/16-1); fixed(1,1); fixed(1,1); fixed(0,1); fixed(0,1); fixed(1,1)
    while (bits.length % 8 != 0) bits.append('0')
    return byteArrayOf(0,0,0,1,0x67) + bits.toString().chunked(8).map { it.toInt(2).toByte() }.toByteArray() +
        byteArrayOf(0,0,0,1,0x68,0x80.toByte())
}
fun main() {
    val hello = VideoProtocol.header(1,32,0)
    check(hello.joinToString("") { "%02x".format(it) } == "545649440101000000000020000000000000000000000000")
    val encoded = VideoProtocol.header(5,1,3,12345,1) + byteArrayOf(9)
    val fragmented = object : InputStream() {
        val source = ByteArrayInputStream(encoded)
        override fun read() = source.read()
        override fun read(b: ByteArray, off: Int, len: Int) = source.read(b, off, minOf(1, len))
    }
    check(VideoProtocol.read(fragmented).payload.contentEquals(byteArrayOf(9)))
    for (length in 0 until encoded.size) rejected { VideoProtocol.read(ByteArrayInputStream(encoded.copyOf(length))) }
    for (length in listOf(0, 1048577, Int.MAX_VALUE, -1)) {
        val header = VideoProtocol.header(5,1,3)
        ByteBuffer.wrap(header).order(ByteOrder.BIG_ENDIAN).putInt(8,length)
        rejected { VideoProtocol.read(ByteArrayInputStream(header)) }
    }
    rejected { VideoProtocol.header(5,1,3,-1) }
    rejected { VideoProtocol.header(2,20,0,1) }
    rejected { VideoProtocol.header(1,32,0,0,1) }
    rejected { VideoProtocol.header(5,1,0x100000000L) }
    var now = 0L
    rejected { VideoProtocol.read(ByteArrayInputStream(encoded), {}, { now += 6_000_000_000; now }) }
    for (fps in listOf(1,15,30,60,120,144,240))
        check(VideoProtocol.format(VideoProtocol.integers(1280,800,fps,3_000_000)).fps == fps)
    rejected { VideoProtocol.format(VideoProtocol.integers(1280,800,0,3_000_000)) }
    rejected { VideoProtocol.format(VideoProtocol.integers(8192,8192,30,3_000_000)) }
    VideoProtocol.decodedSize(1280,816,0,0,1279,799,1280,800)
    rejected { VideoProtocol.decodedSize(1280,816,0,0,1279,815,1280,800) }
    rejected { VideoProtocol.decodedSize(Int.MAX_VALUE,816,0,0,1279,799,1280,800) }
    val bytes = makeSps(1280,800)
    val config = AvcConfig.parse(bytes,1280,800)
    check(config.level == 32)
    rejected { AvcConfig.parse(bytes,1920,1080) }
    rejected { AvcConfig.parse(makeSps(4096,4096),4096,4096) }
    rejected { AvcConfig.parse(byteArrayOf(0,0,1,0x67,66),1280,800) }
    AvcConfig.validateFrame(byteArrayOf(0,0,0,1,0x65,0x80.toByte()),config,true)
    rejected { AvcConfig.validateFrame(byteArrayOf(0,0,0,1,0x61,0x80.toByte()),config,true) }
    rejected { AvcConfig.validateFrame(makeSps(640,480),config,false) }
    rejected { AvcConfig.nals(byteArrayOf(1,2,3)) }
    var clock = 0L
    val budget = StreamBudget(144,3_000_000) { clock }
    budget.accept(100); budget.accept(100)
    rejected { budget.accept(100) }
    clock = 1_000_000_000
    budget.accept(100)
    val random = java.util.Random(42)
    repeat(10000) {
        val sample = ByteArray(24); random.nextBytes(sample)
        rejected { VideoProtocol.parseHeader(sample) }
    }
    println("PASS: video golden bytes, partial/oversized/deadline input, FPS 1–240, SPS limits, frame/rate checks and 10000 malformed headers")
}
