package dev.arimodu.krypton.network

import android.os.Build
import android.util.Log
import dev.arimodu.krypton.proto.*
import com.google.protobuf.ByteString
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.atomic.AtomicInteger
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket

sealed class ConnectionState {
    data object Disconnected : ConnectionState()
    data object Connecting : ConnectionState()
    data object Connected : ConnectionState()
    data object Authenticating : ConnectionState()
    data object Authenticated : ConnectionState()
    data class Error(val message: String) : ConnectionState()
}

sealed class KryptonEvent {
    data class ClipboardReceived(val entry: ClipboardEntry, val fromDevice: String) : KryptonEvent()
    data class AuthResult(val success: Boolean, val message: String, val apiKey: String?) : KryptonEvent()
    data class ConnectionError(val message: String) : KryptonEvent()
    data class HeartbeatLatency(val latencyMs: Long) : KryptonEvent()
}

class KryptonClient(
    private val deviceId: String,
    private val deviceName: String
) {
    companion object {
        private const val TAG = "KryptonClient"
        private const val CLIENT_VERSION = "1.0.0"
        private const val PLATFORM = "Android"
        private const val CONNECT_TIMEOUT_MS = 10000
        private const val HEARTBEAT_INTERVAL_MS = 30000L
        private const val HEADER_SIZE = 6
        private const val MAX_PACKET_SIZE = 10 * 1024 * 1024 // 10 MB
    }

    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private var socket: Socket? = null
    private var inputStream: DataInputStream? = null
    private var outputStream: DataOutputStream? = null

    private val sequenceId = AtomicInteger(0)
    private var readJob: Job? = null
    private var heartbeatJob: Job? = null
    private var lastHeartbeatSentTime: Long = 0L

    private val _connectionState = MutableStateFlow<ConnectionState>(ConnectionState.Disconnected)
    val connectionState: StateFlow<ConnectionState> = _connectionState

    private val _events = MutableSharedFlow<KryptonEvent>(extraBufferCapacity = 64)
    val events: SharedFlow<KryptonEvent> = _events

    private val writeLock = Any()

    suspend fun connect(host: String, port: Int): Boolean = withContext(Dispatchers.IO) {
        try {
            _connectionState.value = ConnectionState.Connecting

            // Create plain TCP socket first
            val plainSocket = Socket()
            plainSocket.connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
            plainSocket.soTimeout = 0 // No read timeout for normal operation

            socket = plainSocket
            inputStream = DataInputStream(plainSocket.getInputStream())
            outputStream = DataOutputStream(plainSocket.getOutputStream())

            // Read ServerHello
            val serverHello = readPacket()
            if (serverHello?.type != PacketType.SERVER_HELLO) {
                throw IOException("Expected ServerHello, got ${serverHello?.type}")
            }
            Log.i(TAG, "Server version: ${serverHello.serverHello.serverVersion}, TLS available: ${serverHello.serverHello.tlsAvailable}")

            // Upgrade to TLS if available
            if (serverHello.serverHello.tlsAvailable) {
                if (!upgradeToTls(host, port)) {
                    Log.w(TAG, "TLS upgrade failed, continuing without TLS")
                }
            }

            // Send Connect
            val connectPacket = createPacket(PacketType.CONNECT) {
                connect = Connect.newBuilder()
                    .setClientVersion(CLIENT_VERSION)
                    .setPlatform(PLATFORM)
                    .setDeviceId(deviceId)
                    .setDeviceName(deviceName)
                    .build()
            }
            sendPacket(connectPacket)

            // Read ConnectAck
            val connectAck = readPacket()
            if (connectAck?.type != PacketType.CONNECT_ACK) {
                throw IOException("Expected ConnectAck, got ${connectAck?.type}")
            }

            _connectionState.value = ConnectionState.Connected

            // Start read loop
            startReadLoop()

            // Start heartbeat
            startHeartbeat()

            true
        } catch (e: Exception) {
            Log.e(TAG, "Connection failed", e)
            _connectionState.value = ConnectionState.Error(e.message ?: "Connection failed")
            disconnect()
            false
        }
    }

    private suspend fun upgradeToTls(host: String, port: Int): Boolean {
        return try {
            // Send StartTls request
            val startTlsPacket = createPacket(PacketType.START_TLS) {
                startTls = StartTls.getDefaultInstance()
            }
            sendPacket(startTlsPacket)

            // Read StartTlsAck
            val ack = readPacket()
            if (ack?.type != PacketType.START_TLS_ACK || !ack.startTlsAck.success) {
                Log.w(TAG, "StartTls rejected: ${ack?.startTlsAck?.message}")
                return false
            }

            // Upgrade to TLS
            val sslContext = SSLContext.getInstance("TLS")
            sslContext.init(null, null, null)
            val sslSocketFactory = sslContext.socketFactory

            val sslSocket = sslSocketFactory.createSocket(
                socket,
                host,
                port,
                true
            ) as SSLSocket

            sslSocket.useClientMode = true
            sslSocket.startHandshake()

            socket = sslSocket
            inputStream = DataInputStream(sslSocket.getInputStream())
            outputStream = DataOutputStream(sslSocket.getOutputStream())

            Log.i(TAG, "TLS upgrade successful")
            true
        } catch (e: Exception) {
            Log.e(TAG, "TLS upgrade failed", e)
            false
        }
    }

    suspend fun authenticateWithApiKey(apiKey: String): Boolean = withContext(Dispatchers.IO) {
        try {
            _connectionState.value = ConnectionState.Authenticating

            val authPacket = createPacket(PacketType.AUTH_API_KEY) {
                authApiKey = AuthApiKey.newBuilder()
                    .setApiKey(apiKey)
                    .build()
            }
            sendPacket(authPacket)

            // Result will come via event
            true
        } catch (e: Exception) {
            Log.e(TAG, "Authentication failed", e)
            _connectionState.value = ConnectionState.Error(e.message ?: "Authentication failed")
            false
        }
    }

    suspend fun authenticateWithCredentials(username: String, password: String): Boolean = withContext(Dispatchers.IO) {
        try {
            _connectionState.value = ConnectionState.Authenticating

            val authPacket = createPacket(PacketType.AUTH_LOGIN) {
                authLogin = AuthLogin.newBuilder()
                    .setUsername(username)
                    .setPassword(password)
                    .build()
            }
            sendPacket(authPacket)

            // Result will come via event
            true
        } catch (e: Exception) {
            Log.e(TAG, "Authentication failed", e)
            _connectionState.value = ConnectionState.Error(e.message ?: "Authentication failed")
            false
        }
    }

    suspend fun pushClipboard(content: String) = withContext(Dispatchers.IO) {
        try {
            val contentBytes = content.toByteArray(Charsets.UTF_8)
            val preview = if (content.length > 200) content.substring(0, 200) else content
            val hash = computeSha256(contentBytes)

            val entry = ClipboardEntry.newBuilder()
                .setContentType(ClipboardContentType.TEXT)
                .setContent(ByteString.copyFrom(contentBytes))
                .setContentPreview(preview)
                .setCreatedAt(System.currentTimeMillis())
                .setSourceDevice(deviceName)
                .setContentHash(hash)
                .build()

            val packet = createPacket(PacketType.CLIPBOARD_PUSH) {
                clipboardPush = ClipboardPush.newBuilder()
                    .setEntry(entry)
                    .build()
            }
            sendPacket(packet)
            Log.d(TAG, "Clipboard pushed: ${preview.take(50)}...")
        } catch (e: Exception) {
            Log.e(TAG, "Failed to push clipboard", e)
        }
    }

    private fun computeSha256(data: ByteArray): String {
        val digest = MessageDigest.getInstance("SHA-256")
        val hash = digest.digest(data)
        return hash.joinToString("") { "%02x".format(it) }
    }

    private fun startReadLoop() {
        readJob = scope.launch {
            try {
                while (isActive && socket?.isConnected == true) {
                    val packet = readPacket() ?: continue
                    handlePacket(packet)
                }
            } catch (e: Exception) {
                if (isActive) {
                    Log.e(TAG, "Read loop error", e)
                    _events.emit(KryptonEvent.ConnectionError(e.message ?: "Connection lost"))
                    disconnect()
                }
            }
        }
    }

    private fun startHeartbeat() {
        heartbeatJob = scope.launch {
            while (isActive && socket?.isConnected == true) {
                delay(HEARTBEAT_INTERVAL_MS)
                try {
                    lastHeartbeatSentTime = System.currentTimeMillis()
                    val heartbeat = createPacket(PacketType.HEARTBEAT) {
                        this.heartbeat = Heartbeat.getDefaultInstance()
                    }
                    sendPacket(heartbeat)
                } catch (e: Exception) {
                    Log.e(TAG, "Heartbeat failed", e)
                }
            }
        }
    }

    private suspend fun handlePacket(packet: KryptonPacket) {
        when (packet.type) {
            PacketType.AUTH_RESPONSE -> {
                val response = packet.authResponse
                if (response.success) {
                    _connectionState.value = ConnectionState.Authenticated
                    Log.i(TAG, "Authentication successful")
                } else {
                    _connectionState.value = ConnectionState.Connected
                    Log.w(TAG, "Authentication failed: ${response.message}")
                }
                _events.emit(KryptonEvent.AuthResult(
                    response.success,
                    response.message,
                    if (response.apiKey.isNotEmpty()) response.apiKey else null
                ))
            }

            PacketType.CLIPBOARD_BROADCAST -> {
                val broadcast = packet.clipboardBroadcast
                Log.i(TAG, "Clipboard broadcast received from ${broadcast.fromDevice}")
                _events.emit(KryptonEvent.ClipboardReceived(broadcast.entry, broadcast.fromDevice))
            }

            PacketType.HEARTBEAT_ACK -> {
                val latency = System.currentTimeMillis() - lastHeartbeatSentTime
                Log.d(TAG, "Heartbeat acknowledged, latency: ${latency}ms")
                _events.emit(KryptonEvent.HeartbeatLatency(latency))
            }

            PacketType.CLIPBOARD_PUSH_ACK -> {
                val ack = packet.clipboardPushAck
                Log.d(TAG, "Clipboard push ${if (ack.success) "successful" else "failed"}: ${ack.message}")
            }

            PacketType.ERROR -> {
                val error = packet.error
                Log.e(TAG, "Server error: ${error.code} - ${error.message}")
                _events.emit(KryptonEvent.ConnectionError("Server error: ${error.message}"))
            }

            PacketType.DISCONNECT -> {
                Log.i(TAG, "Server disconnected: ${packet.disconnect.reason}")
                disconnect()
            }

            else -> {
                Log.d(TAG, "Unhandled packet type: ${packet.type}")
            }
        }
    }

    private fun createPacket(type: PacketType, builder: KryptonPacket.Builder.() -> Unit): KryptonPacket {
        return KryptonPacket.newBuilder()
            .setType(type)
            .setTimestamp(System.currentTimeMillis().toULong().toLong())
            .setSequenceId(sequenceId.incrementAndGet())
            .apply(builder)
            .build()
    }

    private fun sendPacket(packet: KryptonPacket) {
        // Extract the inner message based on packet type
        val payload: ByteArray = when (packet.type) {
            PacketType.HEARTBEAT -> packet.heartbeat.toByteArray()
            PacketType.CONNECT -> packet.connect.toByteArray()
            PacketType.START_TLS -> packet.startTls.toByteArray()
            PacketType.AUTH_LOGIN -> packet.authLogin.toByteArray()
            PacketType.AUTH_REGISTER -> packet.authRegister.toByteArray()
            PacketType.AUTH_API_KEY -> packet.authApiKey.toByteArray()
            PacketType.AUTH_LOGOUT -> packet.authLogout.toByteArray()
            PacketType.CLIPBOARD_PUSH -> packet.clipboardPush.toByteArray()
            PacketType.CLIPBOARD_PULL -> packet.clipboardPull.toByteArray()
            PacketType.DISCONNECT -> packet.disconnect.toByteArray()
            else -> {
                Log.w(TAG, "Unhandled packet type for sending: ${packet.type}")
                ByteArray(0)
            }
        }

        synchronized(writeLock) {
            outputStream?.let { out ->
                // Write 4-byte length (big-endian) - payload + 2 bytes for type
                out.writeInt(payload.size + 2)
                // Write 2-byte packet type (big-endian)
                out.writeShort(packet.type.number)
                // Write payload
                if (payload.isNotEmpty()) {
                    out.write(payload)
                }
                out.flush()
            }
        }
    }

    private fun readPacket(): KryptonPacket? {
        val input = inputStream ?: return null

        // Read 4-byte length (big-endian)
        val length = input.readInt()
        if (length < 2 || length > MAX_PACKET_SIZE) {
            throw IOException("Invalid packet length: $length")
        }

        // Read 2-byte packet type (big-endian)
        val packetTypeNum = input.readUnsignedShort()
        val packetType = PacketType.forNumber(packetTypeNum)
            ?: throw IOException("Unknown packet type: $packetTypeNum")

        // Read payload
        val payloadLength = length - 2
        val payload = ByteArray(payloadLength)
        if (payloadLength > 0) {
            input.readFully(payload)
        }

        // Parse the raw message based on packet type and wrap in KryptonPacket
        val packetBuilder = KryptonPacket.newBuilder()
            .setType(packetType)
            .setTimestamp(System.currentTimeMillis())

        when (packetType) {
            PacketType.SERVER_HELLO -> {
                packetBuilder.serverHello = ServerHello.parseFrom(payload)
            }
            PacketType.START_TLS_ACK -> {
                packetBuilder.startTlsAck = StartTlsAck.parseFrom(payload)
            }
            PacketType.CONNECT_ACK -> {
                packetBuilder.connectAck = ConnectAck.parseFrom(payload)
            }
            PacketType.AUTH_RESPONSE -> {
                packetBuilder.authResponse = AuthResponse.parseFrom(payload)
            }
            PacketType.CLIPBOARD_BROADCAST -> {
                packetBuilder.clipboardBroadcast = ClipboardBroadcast.parseFrom(payload)
            }
            PacketType.CLIPBOARD_PUSH_ACK -> {
                packetBuilder.clipboardPushAck = ClipboardPushAck.parseFrom(payload)
            }
            PacketType.HEARTBEAT_ACK -> {
                packetBuilder.heartbeatAck = HeartbeatAck.parseFrom(payload)
            }
            PacketType.DISCONNECT -> {
                packetBuilder.disconnect = Disconnect.parseFrom(payload)
            }
            PacketType.ERROR -> {
                packetBuilder.error = ErrorResponse.parseFrom(payload)
            }
            else -> {
                Log.w(TAG, "Unhandled packet type for parsing: $packetType")
            }
        }

        return packetBuilder.build()
    }

    fun disconnect() {
        readJob?.cancel()
        heartbeatJob?.cancel()

        try {
            socket?.close()
        } catch (e: Exception) {
            Log.e(TAG, "Error closing socket", e)
        }

        socket = null
        inputStream = null
        outputStream = null

        _connectionState.value = ConnectionState.Disconnected
    }

    fun shutdown() {
        disconnect()
        scope.cancel()
    }
}
