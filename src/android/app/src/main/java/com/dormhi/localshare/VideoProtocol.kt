// SPDX-License-Identifier: Apache-2.0
package com.dormhi.localshare

import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

internal object VideoProtocol {
    const val HEADER_SIZE = 24
    const val MAX_FRAME = 1_048_576
    const val MAX_CONFIG = 65_536
    // Parser/work ceiling, not a supported-rate claim or a 30/60 FPS product cap.
    const val MAX_FPS = 1000
    const val WIDTH = 1280
    const val HEIGHT = 800
    const val HELLO = 1
    const val CAPS = 2
    const val FORMAT = 3
    const val CONFIG = 4
    const val FRAME = 5
    const val ACK = 6
    const val STOP = 7
    const val ERROR = 8
    data class Header(val type: Int, val flags: Int, val length: Int, val sequence: Long, val ptsUs: Long)
    data class Packet(val header: Header, val payload: ByteArray)
    data class Format(val width: Int, val height: Int, val fps: Int, val bitrate: Int)

    fun parseHeader(bytes: ByteArray): Header {
        requireWire(bytes.size == HEADER_SIZE, "header size")
        val b = ByteBuffer.wrap(bytes).order(ByteOrder.BIG_ENDIAN)
        requireWire(b.int == 0x54564944 && b.get().toInt() == 1, "video protocol version") // TVID v1
        val type = b.get().toInt() and 255
        val flags = b.short.toInt() and 65535
        val length = b.int.toLong() and 0xffffffffL
        val sequence = b.int.toLong() and 0xffffffffL
        val pts = b.long
        requireWire(type in HELLO..ERROR && flags in 0..1 && (type == FRAME || flags == 0), "type/flags")
        val validLength = when (type) {
            HELLO -> length == 32L
            CAPS -> length == 20L
            FORMAT -> length == 16L
            CONFIG -> length in 1..MAX_CONFIG.toLong()
            FRAME -> length in 1..MAX_FRAME.toLong()
            ACK, STOP -> length == 0L
            else -> length == 1L
        }
        requireWire(validLength && pts >= 0 && (type == FRAME || pts == 0L), "length/timestamp")
        return Header(type, flags, length.toInt(), sequence, pts)
    }
    fun header(type: Int, length: Int, sequence: Long, ptsUs: Long = 0, flags: Int = 0): ByteArray {
        requireWire(sequence in 0..0xffffffffL, "sequence range")
        val bytes = ByteBuffer.allocate(HEADER_SIZE).order(ByteOrder.BIG_ENDIAN)
            .putInt(0x54564944).put(1).put(type.toByte()).putShort(flags.toShort())
            .putInt(length).putInt(sequence.toInt()).putLong(ptsUs).array()
        parseHeader(bytes)
        return bytes
    }
    fun read(input: InputStream, beforeRead: (Int) -> Unit = {}, now: () -> Long = System::nanoTime): Packet {
        val deadline = now() + 5_000_000_000L
        fun exact(size: Int): ByteArray {
            val result = ByteArray(size)
            var offset = 0
            while (offset < size) {
                val remaining = deadline - now()
                requireWire(remaining > 0, "video packet deadline")
                beforeRead(((remaining / 1_000_000L).coerceIn(1, 5000)).toInt())
                val count = input.read(result, offset, size - offset)
                requireWire(count > 0, "truncated video packet")
                offset += count
            }
            return result
        }
        val parsed = parseHeader(exact(HEADER_SIZE)) // validate length before allocation
        return Packet(parsed, exact(parsed.length))
    }
    fun write(output: OutputStream, type: Int, sequence: Long, payload: ByteArray = byteArrayOf()) {
        output.write(header(type, payload.size, sequence))
        output.write(payload)
        output.flush()
    }
    fun format(bytes: ByteArray): Format {
        requireWire(bytes.size == 16, "format size")
        val b = ByteBuffer.wrap(bytes).order(ByteOrder.BIG_ENDIAN)
        val f = Format(b.int, b.int, b.int, b.int)
        requireWire(f.width == WIDTH && f.height == HEIGHT && f.fps in 1..MAX_FPS &&
            f.bitrate in 100_000..50_000_000, "unsupported proof format")
        return f
    }
    fun decodedSize(width: Int, height: Int, left: Int, top: Int, right: Int, bottom: Int,
                    expectedWidth: Int, expectedHeight: Int) {
        requireWire(width in 1..1920 && height in 1..1088 && left >= 0 && top >= 0 &&
            right >= left && right < width && bottom >= top && bottom < height &&
            right.toLong() - left + 1 == expectedWidth.toLong() &&
            bottom.toLong() - top + 1 == expectedHeight.toLong(), "decoder output crop/size")
    }
    fun integers(vararg values: Int): ByteArray = ByteBuffer.allocate(values.size * 4)
        .order(ByteOrder.BIG_ENDIAN).apply { values.forEach { putInt(it) } }.array()
    fun requireWire(valid: Boolean, message: String) { if (!valid) throw IOException(message) }
}

// Validate Annex-B boundaries without a codec dependency. Only Baseline SPS,
// matching dimensions and bounded reference count are accepted for this proof.
internal object AvcConfig {
    data class Config(val sps: ByteArray, val pps: ByteArray, val level: Int)
    fun nals(data: ByteArray): List<ByteArray> {
        fun prefix(i: Int): Int = when {
            i + 3 < data.size && data[i] == 0.toByte() && data[i+1] == 0.toByte() &&
                data[i+2] == 0.toByte() && data[i+3] == 1.toByte() -> 4
            i + 2 < data.size && data[i] == 0.toByte() && data[i+1] == 0.toByte() && data[i+2] == 1.toByte() -> 3
            else -> 0
        }
        val output = ArrayList<ByteArray>()
        var start = 0
        while (start < data.size) {
            val p = prefix(start)
            VideoProtocol.requireWire(p != 0, "Annex-B prefix")
            val content = start + p
            var end = content
            while (end < data.size && prefix(end) == 0) end++
            VideoProtocol.requireWire(end > content && output.size < 64, "NAL count/size")
            output.add(data.copyOfRange(content, end))
            start = end
        }
        return output
    }
    fun parse(data: ByteArray, width: Int, height: Int): Config {
        VideoProtocol.requireWire(data.size in 1..VideoProtocol.MAX_CONFIG, "configuration bound")
        val list = nals(data)
        VideoProtocol.requireWire(list.size == 2 && list[0][0].toInt() and 31 == 7 &&
            list[1][0].toInt() and 31 == 8 && list.all { it[0].toInt() and 128 == 0 }, "SPS/PPS configuration required")
        val sps = list[0]
        VideoProtocol.requireWire(sps.size in 5..16384 && list[1].size in 2..16384, "codec configuration size")
        val rbsp = ArrayList<Byte>()
        var zeros = 0
        for (i in 1 until sps.size) {
            val byte = sps[i].toInt() and 255
            if (zeros == 2 && byte == 3) {
                VideoProtocol.requireWire(i + 1 < sps.size && (sps[i+1].toInt() and 255) <= 3, "escape byte")
                zeros = 0
                continue
            }
            rbsp.add(sps[i])
            zeros = if (byte == 0) zeros + 1 else 0
        }
        val bits = Bits(rbsp.toByteArray())
        VideoProtocol.requireWire(bits.read(8) == 66, "Baseline profile required")
        bits.read(8)
        val level = bits.read(8)
        VideoProtocol.requireWire(level in setOf(10,11,12,13,20,21,22,30,31,32,40,41,42,50,51,52,60,61,62), "AVC level")
        VideoProtocol.requireWire(bits.ue() <= 31 && bits.ue() <= 12, "SPS/frame number")
        when (bits.ue()) {
            0 -> VideoProtocol.requireWire(bits.ue() <= 12, "picture order count")
            1 -> {
                bits.read(1); bits.ue(); bits.ue()
                val count = bits.ue()
                VideoProtocol.requireWire(count <= 255, "picture order cycle")
                repeat(count) { bits.ue() }
            }
            2 -> Unit
            else -> throw IOException("picture order type")
        }
        VideoProtocol.requireWire(bits.ue() <= 4, "reference frame bound")
        bits.read(1)
        val columns = bits.ue().toLong() + 1
        val rows = bits.ue().toLong() + 1
        VideoProtocol.requireWire(columns <= 120 && rows <= 68 && bits.read(1) == 1, "frame dimensions/interlace")
        bits.read(1)
        var left = 0; var right = 0; var top = 0; var bottom = 0
        if (bits.read(1) == 1) { left = bits.ue(); right = bits.ue(); top = bits.ue(); bottom = bits.ue() }
        VideoProtocol.requireWire(columns * 16 - 2L * (left.toLong() + right) == width.toLong() &&
            rows * 16 - 2L * (top.toLong() + bottom) == height.toLong(), "SPS size differs from negotiated format")
        val prefix = byteArrayOf(0,0,0,1)
        return Config(prefix + sps, prefix + list[1], level)
    }
    fun validateFrame(data: ByteArray, config: Config, keyframe: Boolean) {
        val list = nals(data)
        var hasSlice = false; var hasIdr = false
        for (nal in list) {
            VideoProtocol.requireWire(nal[0].toInt() and 128 == 0, "forbidden NAL bit")
            when (nal[0].toInt() and 31) {
                1 -> hasSlice = true
                5 -> { hasSlice = true; hasIdr = true }
                7 -> VideoProtocol.requireWire(nal.contentEquals(config.sps.copyOfRange(4, config.sps.size)), "SPS change")
                8 -> VideoProtocol.requireWire(nal.contentEquals(config.pps.copyOfRange(4, config.pps.size)), "PPS change")
                6, 9, 12 -> Unit
                else -> throw IOException("unsupported NAL type")
            }
        }
        VideoProtocol.requireWire(hasSlice && (!keyframe || hasIdr), "frame/keyframe content")
    }
    private class Bits(private val data: ByteArray) {
        private var position = 0
        fun read(count: Int): Int {
            VideoProtocol.requireWire(count in 0..31 && position + count <= data.size * 8, "truncated SPS")
            var value = 0
            repeat(count) {
                value = (value shl 1) or ((data[position / 8].toInt() ushr (7 - position % 8)) and 1)
                position++
            }
            return value
        }
        fun ue(): Int {
            var zeros = 0
            while (read(1) == 0) { zeros++; VideoProtocol.requireWire(zeros <= 20, "SPS integer bound") }
            return (1 shl zeros) - 1 + read(zeros)
        }
    }
}

internal class StreamBudget(private val fps: Int, bitrate: Int, private val now: () -> Long = System::nanoTime) {
    private val bytesPerSecond = bitrate / 8.0
    private val byteCapacity = VideoProtocol.MAX_FRAME + bytesPerSecond * 2
    private var bytes = byteCapacity
    private var frames = 2.0
    private var last = now()
    fun accept(size: Int) {
        val time = now()
        val elapsed = (time - last).coerceAtLeast(0) / 1_000_000_000.0
        last = time
        frames = minOf(2.0, frames + elapsed * fps)
        bytes = minOf(byteCapacity, bytes + elapsed * bytesPerSecond)
        VideoProtocol.requireWire(frames >= 1 && size in 1..VideoProtocol.MAX_FRAME && bytes >= size, "stream rate exceeded")
        frames -= 1; bytes -= size
    }
}
