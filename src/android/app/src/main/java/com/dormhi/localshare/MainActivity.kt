package com.dormhi.localshare

import android.net.LocalServerSocket
import android.net.LocalSocket
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import java.io.IOException
import java.net.SocketTimeoutException
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicBoolean

private const val SOCKET_NAME = "com.dormhi.localshare.session"
private const val HANDSHAKE_SIZE = 40
private val STOP_MESSAGE = byteArrayOf('T'.code.toByte(), 'D'.code.toByte(), 'S'.code.toByte(), 'H'.code.toByte(), 1, 3, 0, 0)
private val PING_MESSAGE = byteArrayOf('T'.code.toByte(), 'D'.code.toByte(), 'S'.code.toByte(), 'H'.code.toByte(), 1, 4, 0, 0)
private val PONG_MESSAGE = byteArrayOf('T'.code.toByte(), 'D'.code.toByte(), 'S'.code.toByte(), 'H'.code.toByte(), 1, 5, 0, 0)

class MainActivity : ComponentActivity() {
    @Volatile private var server: LocalServerSocket? = null
    @Volatile private var activeClient: LocalSocket? = null
    @Volatile private var endpointThread: Thread? = null
    @Volatile private var endpointStop = AtomicBoolean(true)
    private var status by mutableStateOf("Waiting for the Windows host")

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    Column(
                        modifier = Modifier.fillMaxSize().padding(24.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.Center,
                    ) {
                        Text("Tablet Display", style = MaterialTheme.typography.headlineMedium)
                        Text(status, modifier = Modifier.padding(top = 12.dp))
                        Text(
                            "Connect by USB and authorize this computer in the Android USB debugging prompt.",
                            modifier = Modifier.padding(top = 12.dp),
                        )
                    }
                }
            }
        }
        startEndpoint(consumeSessionToken())
    }

    override fun onNewIntent(intent: android.content.Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        consumeSessionToken()?.let { token ->
            stopEndpoint()
            startEndpoint(token)
        }
    }

    override fun onDestroy() {
        stopEndpoint()
        super.onDestroy()
    }

    private fun startEndpoint(expectedToken: ByteArray?) {
        if (expectedToken == null) {
            status = "Waiting for the Windows host to start a USB session"
            return
        }
        val stop = AtomicBoolean(false)
        synchronized(this) { endpointStop = stop }
        status = "USB host authorized; waiting for handshake"
        val worker = Thread({
            try {
                acceptLoop(expectedToken, stop)
            } finally {
                synchronized(this) {
                    if (endpointThread === Thread.currentThread()) endpointThread = null
                }
            }
        }, "localshare-adb-endpoint")
        synchronized(this) {
            if (endpointStop === stop) {
                endpointThread = worker
                worker.isDaemon = true
                worker.start()
            }
        }
    }

    private fun acceptLoop(expectedToken: ByteArray, stop: AtomicBoolean) {
        try {
            val listener = LocalServerSocket(SOCKET_NAME)
            val keepListener = synchronized(this) {
                if (endpointStop !== stop || stop.get()) false
                else {
                    server = listener
                    true
                }
            }
            if (!keepListener) {
                listener.close()
                return
            }
            try {
                while (!stop.get()) {
                    val client = try {
                        listener.accept()
                    } catch (_: IOException) {
                        if (stop.get()) break else continue
                    }
                    val keepClient = synchronized(this) {
                        if (endpointStop !== stop || stop.get()) false
                        else {
                            activeClient = client
                            true
                        }
                    }
                    if (!keepClient) {
                        runCatching { client.close() }
                        break
                    }
                    try {
                        client.soTimeout = 5_000
                        if (performHandshake(client, expectedToken)) {
                            runOnUiThread { if (endpointStop === stop && !stop.get()) status = "Connected to Windows host" }
                            // Phase 3 proves the authorized tunnel only; no image data is sent.
                            var stopRequested = false
                            while (!stop.get()) {
                                try {
                                    val control = ByteArray(8)
                                    client.inputStream.readFully(control)
                                    if (control.contentEquals(STOP_MESSAGE)) {
                                        stopRequested = true
                                        break
                                    }
                                    if (!control.contentEquals(PING_MESSAGE)) break
                                    client.outputStream.write(PONG_MESSAGE)
                                    client.outputStream.flush()
                                } catch (_: SocketTimeoutException) {
                                    // Recheck lifecycle and peer status every five seconds.
                                } catch (_: IOException) {
                                    break
                                }
                            }
                            if (stopRequested) {
                                stopEndpoint()
                                runOnUiThread { if (endpointStop === stop) status = "Disconnected" }
                            }
                        }
                    } catch (_: Exception) {
                        // Reject malformed, incomplete, or idle clients without exposing data.
                    } finally {
                        runCatching { client.close() }
                        synchronized(this) {
                            if (activeClient === client) activeClient = null
                        }
                        if (!stop.get()) runOnUiThread { if (endpointStop === stop) status = "Waiting for the Windows host" }
                    }
                }
            } finally {
                runCatching { listener.close() }
                synchronized(this) {
                    if (server === listener) server = null
                }
            }
        } catch (_: IOException) {
            if (!stop.get()) runOnUiThread { if (endpointStop === stop) status = "Could not open the local ADB endpoint" }
        }
    }

    private fun performHandshake(client: LocalSocket, expectedToken: ByteArray): Boolean {
        val request = ByteArray(HANDSHAKE_SIZE)
        client.inputStream.readFully(request)
        val validHeader = request[0] == 'T'.code.toByte() && request[1] == 'D'.code.toByte() &&
            request[2] == 'S'.code.toByte() && request[3] == 'H'.code.toByte() &&
            request[4] == 1.toByte() && request[5] == 1.toByte() &&
            request[6] == 0.toByte() && request[7] == 32.toByte()
        val receivedToken = request.copyOfRange(8, HANDSHAKE_SIZE)
        if (!validHeader || !MessageDigest.isEqual(receivedToken, expectedToken)) return false
        client.outputStream.write(byteArrayOf('T'.code.toByte(), 'D'.code.toByte(), 'S'.code.toByte(), 'H'.code.toByte(), 1, 2, 0, 0))
        client.outputStream.flush()
        return true
    }

    private fun stopEndpoint() {
        val resources = synchronized(this) {
            endpointStop.set(true)
            val current = Triple(activeClient, server, endpointThread)
            activeClient = null
            server = null
            current
        }
        runCatching { resources.first?.close() }
        runCatching { resources.second?.close() }
        resources.third?.let { thread ->
            if (thread !== Thread.currentThread()) runCatching { thread.join(1_000) }
        }
    }

    private fun decodeToken(value: String): ByteArray? {
        if (!value.matches(Regex("[0-9a-fA-F]{64}"))) return null
        return runCatching { value.chunked(2).map { it.toInt(16).toByte() }.toByteArray() }.getOrNull()
    }

    private fun consumeSessionToken(): ByteArray? {
        val file = java.io.File(filesDir, "session_token")
        val value = runCatching { file.readText().trim() }.getOrNull() ?: return null
        runCatching { file.delete() }
        return decodeToken(value)
    }
}

private fun java.io.InputStream.readFully(destination: ByteArray) {
    var offset = 0
    while (offset < destination.size) {
        val count = read(destination, offset, destination.size - offset)
        if (count < 0) throw IOException("incomplete handshake")
        offset += count
    }
}
