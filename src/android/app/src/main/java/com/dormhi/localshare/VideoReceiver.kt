// SPDX-License-Identifier: Apache-2.0
package com.dormhi.localshare

import android.net.LocalServerSocket
import android.net.LocalSocket
import android.view.Surface
import java.io.Closeable
import java.io.IOException
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicBoolean

internal class VideoReceiver(
    private val token: ByteArray,
    private val surface: () -> Surface?,
    private val panelRates: () -> IntArray,
    private val status: (String) -> Unit,
) : Closeable {
    private val stopped = AtomicBoolean(false)
    @Volatile private var listener: LocalServerSocket? = null
    @Volatile private var client: LocalSocket? = null
    private val worker = Thread({ serve() }, "localshare-video-endpoint").apply { isDaemon = true }
    fun start() { worker.start() }
    private fun read(socket: LocalSocket) = VideoProtocol.read(socket.inputStream, { socket.soTimeout = it })
    private fun serve() {
        try {
            val server = LocalServerSocket("com.dormhi.localshare.video")
            listener = server
            if (stopped.get()) return
            while (!stopped.get()) {
                val socket = server.accept()
                client = socket
                var authenticated = false
                var decoder: VideoDecoder? = null
                try {
                    if (stopped.get()) break
                    val hello = read(socket)
                    VideoProtocol.requireWire(hello.header.type == VideoProtocol.HELLO && hello.header.sequence == 0L &&
                        MessageDigest.isEqual(hello.payload, token), "video authentication")
                    authenticated = true
                    val rates = panelRates()
                    VideoProtocol.write(socket.outputStream, VideoProtocol.CAPS, 0,
                        VideoProtocol.integers(VideoProtocol.WIDTH, VideoProtocol.HEIGHT,
                            VideoDecoder.maximumFps(), rates[0], rates[1]))
                    val request = read(socket)
                    VideoProtocol.requireWire(request.header.type == VideoProtocol.FORMAT && request.header.sequence == 1L, "format order")
                    val format = VideoProtocol.format(request.payload)
                    VideoProtocol.requireWire(VideoDecoder.supports(format), "decoder size/rate unsupported")
                    VideoProtocol.write(socket.outputStream, VideoProtocol.ACK, 1)
                    val configuration = read(socket)
                    VideoProtocol.requireWire(configuration.header.type == VideoProtocol.CONFIG && configuration.header.sequence == 2L, "configuration order")
                    val config = AvcConfig.parse(configuration.payload, format.width, format.height)
                    decoder = VideoDecoder(format, config, surface() ?: throw IOException("Display surface unavailable")) {
                        runCatching { socket.close() }
                    }
                    VideoProtocol.write(socket.outputStream, VideoProtocol.ACK, 2)
                    status("Test pattern: ${format.fps} FPS limit; panel currently ${rates[0] / 1000f} Hz (reported)")
                    val budget = StreamBudget(format.fps, format.bitrate)
                    var sequence = 3L
                    var lastPts = -1L
                    while (!stopped.get()) {
                        val packet = read(socket)
                        VideoProtocol.requireWire(packet.header.sequence == sequence, "video sequence")
                        if (packet.header.type == VideoProtocol.STOP) break
                        VideoProtocol.requireWire(packet.header.type == VideoProtocol.FRAME && packet.header.ptsUs > lastPts,
                            "video frame/timestamp order")
                        val key = packet.header.flags == 1
                        VideoProtocol.requireWire(sequence != 3L || key, "first frame must be IDR")
                        budget.accept(packet.payload.size)
                        AvcConfig.validateFrame(packet.payload, config, key)
                        decoder.queue(packet.payload, packet.header.ptsUs)
                        VideoProtocol.write(socket.outputStream, VideoProtocol.ACK, sequence)
                        sequence++
                        lastPts = packet.header.ptsUs
                        VideoProtocol.requireWire(sequence <= 0xffffffffL, "sequence exhausted")
                    }
                } catch (_: Exception) {
                    if (!stopped.get() && authenticated) {
                        runCatching { VideoProtocol.write(socket.outputStream, VideoProtocol.ERROR, 0, byteArrayOf(1)) }
                        status("Video stopped: unsupported format, invalid data, or decoder too slow. Reconnect with a lower FPS limit if needed.")
                    }
                } finally {
                    decoder?.close()
                    runCatching { socket.close() }
                    client = null
                }
            }
        } catch (_: IOException) {
            if (!stopped.get()) status("Video endpoint unavailable; reconnect the USB session")
        } finally {
            runCatching { client?.close() }; runCatching { listener?.close() }
            token.fill(0)
        }
    }
    override fun close() {
        if (!stopped.compareAndSet(false, true)) return
        runCatching { client?.close() }; runCatching { listener?.close() }
    }
}
