// SPDX-License-Identifier: Apache-2.0
package com.dormhi.localshare

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaCodecList
import android.media.MediaFormat
import android.os.Handler
import android.os.HandlerThread
import android.view.Surface
import java.io.Closeable
import java.io.IOException
import java.nio.ByteBuffer
import java.util.ArrayDeque
import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

internal class VideoDecoder(
    private val format: VideoProtocol.Format,
    config: AvcConfig.Config,
    surface: Surface,
    private val onFailure: () -> Unit,
) : Closeable {
    companion object {
        private val occupied = AtomicBoolean(false)
        private fun candidates() = MediaCodecList(MediaCodecList.REGULAR_CODECS).codecInfos.filter {
            !it.isEncoder && it.supportedTypes.any { type -> type.equals(MediaFormat.MIMETYPE_VIDEO_AVC, true) }
        }
        fun maximumFps(): Int = candidates().mapNotNull { info ->
            runCatching {
                val caps = info.getCapabilitiesForType(MediaFormat.MIMETYPE_VIDEO_AVC)
                if (caps.profileLevels.none { it.profile == MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline }) return@runCatching 0
                (caps.videoCapabilities ?: return@runCatching 0).getSupportedFrameRatesFor(VideoProtocol.WIDTH, VideoProtocol.HEIGHT)
                    .upper.toInt().coerceIn(0, VideoProtocol.MAX_FPS)
            }.getOrNull()
        }.maxOrNull() ?: 0
        fun supports(format: VideoProtocol.Format): Boolean = candidates().any { info ->
            runCatching {
                val caps = info.getCapabilitiesForType(MediaFormat.MIMETYPE_VIDEO_AVC)
                caps.profileLevels.any { it.profile == MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline } &&
                    (caps.videoCapabilities?.areSizeAndRateSupported(format.width, format.height, format.fps.toDouble()) == true)
            }.getOrDefault(false)
        }
    }
    private val thread: HandlerThread
    private val handler: Handler
    private var codec: MediaCodec? = null // accessed only on the actor
    private val available = ArrayDeque<Int>()
    private var pending: Pending? = null
    private var failed: Throwable? = null
    private val closed = AtomicBoolean(false)
    private data class Pending(val bytes: ByteArray, val pts: Long, val done: CompletableFuture<Unit>)

    init {
        if (!occupied.compareAndSet(false, true)) throw IOException("Previous decoder is still closing")
        thread = HandlerThread("localshare-video-codec")
        try { thread.start(); handler = Handler(thread.looper) }
        catch (error: Throwable) { occupied.set(false); throw error }
        val ready = CompletableFuture<Unit>()
        handler.post {
            try {
                if (!surface.isValid) throw IOException("Display surface unavailable")
                val media = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, format.width, format.height)
                media.setInteger(MediaFormat.KEY_FRAME_RATE, format.fps)
                media.setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline)
                media.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, VideoProtocol.MAX_FRAME)
                // AVC level is a power-of-two Android enum, not the SPS level_idc.
                val levels = listOf(10,11,12,13,20,21,22,30,31,32,40,41,42,50,51,52,60,61,62)
                val index = levels.indexOf(config.level)
                if (index < 0) throw IOException("Unsupported AVC level")
                val androidLevel = if (index == 0) 1 else 1 shl (index + 1) // skip Level1b
                media.setInteger(MediaFormat.KEY_LEVEL, androidLevel)
                media.setByteBuffer("csd-0", ByteBuffer.wrap(config.sps))
                media.setByteBuffer("csd-1", ByteBuffer.wrap(config.pps))
                val name = MediaCodecList(MediaCodecList.REGULAR_CODECS).findDecoderForFormat(media)
                    ?: throw IOException("No decoder supports the negotiated format")
                val decoder = MediaCodec.createByCodecName(name)
                codec = decoder
                decoder.setCallback(object : MediaCodec.Callback() {
                    override fun onInputBufferAvailable(codec: MediaCodec, index: Int) {
                        if (closed.get() || failed != null) return
                        if (available.size >= 64) { fail(IOException("Decoder input queue bound")); return }
                        available.addLast(index)
                        submit()
                    }
                    override fun onOutputBufferAvailable(codec: MediaCodec, index: Int, info: MediaCodec.BufferInfo) {
                        if (closed.get() || failed != null) return
                        runCatching { codec.releaseOutputBuffer(index, info.size > 0) }.onFailure { fail(it) }
                    }
                    override fun onOutputFormatChanged(codec: MediaCodec, media: MediaFormat) {
                        try {
                            val width = media.getInteger(MediaFormat.KEY_WIDTH)
                            val height = media.getInteger(MediaFormat.KEY_HEIGHT)
                            fun field(name: String, fallback: Int) = if (media.containsKey(name)) media.getInteger(name) else fallback
                            VideoProtocol.decodedSize(width, height, field("crop-left", 0), field("crop-top", 0),
                                field("crop-right", width - 1), field("crop-bottom", height - 1), format.width, format.height)
                        } catch (error: Throwable) { fail(error) }
                    }
                    override fun onError(codec: MediaCodec, error: MediaCodec.CodecException) { fail(error) }
                }, handler)
                decoder.configure(media, surface, null, 0)
                decoder.start()
                ready.complete(Unit)
            } catch (error: Throwable) { ready.completeExceptionally(error); fail(error) }
        }
        try { ready.get(5, TimeUnit.SECONDS) }
        catch (error: Exception) { close(); throw IOException("Could not start the decoder", error) }
    }
    private fun fail(error: Throwable) {
        if (failed != null) return
        failed = error
        pending?.done?.completeExceptionally(IOException("Decoder failed"))
        pending = null
        onFailure() // closes the local socket, waking its worker; no image/codec details logged
    }
    private fun submit() {
        val frame = pending ?: return
        if (available.isEmpty() || closed.get()) return
        try {
            val index = available.removeFirst()
            val decoder = codec ?: throw IOException("Decoder unavailable")
            val buffer = decoder.getInputBuffer(index) ?: throw IOException("Missing decoder input")
            if (buffer.capacity() < frame.bytes.size) throw IOException("Decoder input too small")
            buffer.clear(); buffer.put(frame.bytes)
            decoder.queueInputBuffer(index, 0, frame.bytes.size, frame.pts, 0)
            pending = null
            frame.done.complete(Unit)
        } catch (error: Throwable) { fail(error) }
    }
    fun queue(bytes: ByteArray, pts: Long) {
        val done = CompletableFuture<Unit>()
        if (closed.get() || !handler.post {
            if (closed.get() || failed != null || pending != null) done.completeExceptionally(IOException("Decoder unavailable or busy"))
            else { pending = Pending(bytes, pts, done); submit() }
        }) throw IOException("Decoder closed")
        try { done.get(2, TimeUnit.SECONDS) }
        catch (error: Exception) { throw IOException("Decoder is too slow or unavailable", error) }
    }
    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        handler.post {
            pending?.done?.completeExceptionally(IOException("Decoder stopped")); pending = null
            available.clear()
            try { codec?.let { runCatching { it.stop() }; it.release() } }
            finally { codec = null; occupied.set(false); thread.quitSafely() }
        }
        // If a vendor codec stalls, its slot remains occupied: never create
        // unbounded decoder threads while an old one still owns resources.
    }
}
