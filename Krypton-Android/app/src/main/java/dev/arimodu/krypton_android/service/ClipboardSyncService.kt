package dev.arimodu.krypton.service

import android.R
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Intent
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.provider.Settings
import android.util.Log
import androidx.core.app.NotificationCompat
import java.io.BufferedReader
import java.io.InputStreamReader
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import dev.arimodu.krypton.MainActivity
import dev.arimodu.krypton.data.CredentialStorage
import dev.arimodu.krypton.network.ConnectionState
import dev.arimodu.krypton.network.KryptonClient
import dev.arimodu.krypton.network.KryptonEvent
import dev.arimodu.krypton.proto.ClipboardContentType
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.collectLatest

enum class ClipboardMonitoringType {
    LISTENER,       // Event-driven via OnPrimaryClipChangedListener
    POLLING,        // Polling fallback
    LOGCAT_OVERLAY  // Logcat monitoring with overlay for Android 10+
}

class ClipboardSyncService : Service() {
    companion object {
        private const val TAG = "ClipboardSyncService"
        private const val NOTIFICATION_ID = 1001
        private const val CHANNEL_ID = "krypton_sync_channel"
        private const val POLLING_INTERVAL_MS = 1000L
        private const val RECONNECT_DELAY_MS = 5000L
        private const val MAX_HEARTBEAT_SAMPLES = 5

        const val ACTION_START = "dev.arimodu.krypton.START"
        const val ACTION_STOP = "dev.arimodu.krypton.STOP"

        @Volatile
        private var instance: ClipboardSyncService? = null

        fun isRunning(): Boolean = instance != null

        fun getInstance(): ClipboardSyncService? = instance
    }

    private val serviceScope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private lateinit var credentialStorage: CredentialStorage
    private lateinit var clipboardManager: ClipboardManager
    private var client: KryptonClient? = null

    private val handler = Handler(Looper.getMainLooper())
    private var lastClipboardContent: String? = null
    private var lastReceivedContent: String? = null
    private var lastContentHash: String? = null
    private var isListenerRegistered = false
    private var pollingJob: Job? = null
    private var connectionJob: Job? = null
    private var logcatJob: Job? = null
    private var logcatProcess: Process? = null

    private var currentStatus = "Initializing..."
    private var currentMonitoringType: ClipboardMonitoringType = ClipboardMonitoringType.LISTENER
    private val heartbeatLatencies = mutableListOf<Long>()

    // Public accessors for UI
    fun getStatus(): String = currentStatus
    fun getMonitoringType(): ClipboardMonitoringType = currentMonitoringType
    fun getHeartbeatLatencies(): List<Long> = heartbeatLatencies.toList()
    fun getAverageLatency(): Long = if (heartbeatLatencies.isNotEmpty()) heartbeatLatencies.average().toLong() else 0L

    /**
     * Push clipboard content directly from the foreground (e.g., when user copies via UI button).
     * This bypasses the normal monitoring and directly sends to server.
     */
    fun pushClipboardFromForeground(content: String) {
        if (!credentialStorage.syncEnabled) return

        val hash = computeHash(content)
        if (hash != lastContentHash) {
            lastClipboardContent = content
            lastContentHash = hash
            onNewClipboardContent(content)
        }
    }

    /**
     * Called when the app comes to foreground. Reads and syncs clipboard if changed.
     * This is useful when logcat monitoring isn't available (no READ_LOGS permission).
     */
    fun onAppForeground() {
        if (!credentialStorage.syncEnabled) return

        try {
            val content = getClipboardText()
            if (content != null && content != lastClipboardContent && content != lastReceivedContent) {
                val hash = computeHash(content)
                if (hash != lastContentHash) {
                    Log.d(TAG, "Foreground clipboard sync: ${content.take(50)}...")
                    lastClipboardContent = content
                    lastContentHash = hash
                    onNewClipboardContent(content)
                }
            }
        } catch (e: Exception) {
            Log.w(TAG, "Error reading clipboard on foreground", e)
        }
    }

    private val clipboardListener = ClipboardManager.OnPrimaryClipChangedListener {
        onClipboardChanged()
    }

    override fun onCreate() {
        super.onCreate()
        instance = this
        credentialStorage = CredentialStorage(this)
        clipboardManager = getSystemService(CLIPBOARD_SERVICE) as ClipboardManager

        createNotificationChannels()
        Log.i(TAG, "Service created")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                stopSelf()
                return START_NOT_STICKY
            }
            else -> {
                currentStatus = "Starting..."
                val notification = if (credentialStorage.showNotification) createNotification() else createMinimalNotification()
                startForeground(NOTIFICATION_ID, notification)
                startSyncService()
            }
        }
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        instance = null
        stopSyncService()
        super.onDestroy()
        Log.i(TAG, "Service destroyed")
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        super.onTaskRemoved(rootIntent)
        // Restart service when app is removed from recents
        if (credentialStorage.syncEnabled && credentialStorage.hasCredentials) {
            val restartIntent = Intent(this, ClipboardSyncService::class.java).apply {
                action = ACTION_START
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(restartIntent)
            } else {
                startService(restartIntent)
            }
        }
    }

    private fun startSyncService() {
        // Determine and start the best available monitoring type (only one active at a time)
        selectAndStartMonitoring()

        // Start connection
        connectionJob = serviceScope.launch {
            connectAndMaintain()
        }
    }

    private fun selectAndStartMonitoring() {
        // Stop any existing monitoring
        stopAllMonitoring()

        // On Android 10+, prefer logcat+overlay if permissions are available
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val hasOverlayPermission = Settings.canDrawOverlays(this)
            val hasReadLogsPermission = checkReadLogsPermission()

            if (hasOverlayPermission && hasReadLogsPermission) {
                // Best option for Android 10+: logcat monitoring with overlay
                currentMonitoringType = ClipboardMonitoringType.LOGCAT_OVERLAY
                startLogcatMonitoring()
                Log.i(TAG, "Using LOGCAT_OVERLAY monitoring")
                return
            }
        }

        // Try event-driven listener first (works on Android 9 and below, limited on 10+)
        if (registerClipboardListener()) {
            currentMonitoringType = ClipboardMonitoringType.LISTENER
            Log.i(TAG, "Using LISTENER monitoring")
            return
        }

        // Fall back to polling
        currentMonitoringType = ClipboardMonitoringType.POLLING
        startPollingFallback()
        Log.i(TAG, "Using POLLING monitoring")
    }

    private fun checkReadLogsPermission(): Boolean {
        return try {
            // Try to read logcat - if it works, we have permission
            val process = Runtime.getRuntime().exec(arrayOf("logcat", "-d", "-t", "1"))
            val exitCode = process.waitFor()
            process.destroy()
            exitCode == 0
        } catch (e: Exception) {
            false
        }
    }

    private fun stopAllMonitoring() {
        unregisterClipboardListener()
        pollingJob?.cancel()
        pollingJob = null
        logcatJob?.cancel()
        stopLogcatProcess()
    }

    private fun stopSyncService() {
        stopAllMonitoring()
        connectionJob?.cancel()
        client?.shutdown()
        client = null
        serviceScope.cancel()
    }

    private fun stopLogcatProcess() {
        try {
            logcatProcess?.destroy()
        } catch (e: Exception) {
            Log.w(TAG, "Error stopping logcat process", e)
        }
        logcatProcess = null
    }

    private fun startLogcatMonitoring() {
        logcatJob?.cancel()
        logcatJob = serviceScope.launch {
            try {
                // Get current timestamp for logcat filter
                val dateFormat = SimpleDateFormat("MM-dd HH:mm:ss.SSS", Locale.US)
                val timeStamp = dateFormat.format(Date())

                // Monitor ClipboardService errors
                val process = Runtime.getRuntime().exec(
                    arrayOf("logcat", "-T", timeStamp, "ClipboardService:E", "*:S")
                )
                logcatProcess = process

                val reader = BufferedReader(InputStreamReader(process.inputStream))
                var line: String?

                while (isActive) {
                    line = reader.readLine()
                    if (line == null) break

                    // Check if the log line contains our package name (clipboard access denied)
                    if (line.contains(packageName) || line.contains("dev.arimodu.krypton")) {
                        Log.d(TAG, "Clipboard access denied detected, launching overlay")

                        // Check if overlay permission is granted
                        if (Settings.canDrawOverlays(this@ClipboardSyncService)) {
                            withContext(Dispatchers.Main) {
                                ClipboardFloatingActivity.launch(
                                    this@ClipboardSyncService,
                                    ClipboardFloatingActivity.SOURCE_LOGCAT
                                )
                            }
                        } else {
                            Log.w(TAG, "Overlay permission not granted")
                        }
                    }
                }
            } catch (e: Exception) {
                if (isActive) {
                    Log.e(TAG, "Logcat monitoring error", e)
                }
            }
        }
    }

    /**
     * Called by ClipboardFloatingActivity when it successfully reads clipboard
     */
    fun onClipboardReadFromOverlay(content: String) {
        if (!credentialStorage.syncEnabled) return

        val hash = computeHash(content)
        if (hash != lastContentHash && content != lastReceivedContent) {
            lastClipboardContent = content
            lastContentHash = hash
            onNewClipboardContent(content)
        }
    }

    private fun registerClipboardListener(): Boolean {
        if (!isListenerRegistered) {
            try {
                clipboardManager.addPrimaryClipChangedListener(clipboardListener)
                isListenerRegistered = true
                Log.i(TAG, "Clipboard listener registered")
                return true
            } catch (e: Exception) {
                Log.w(TAG, "Failed to register clipboard listener", e)
                return false
            }
        }
        return isListenerRegistered
    }

    private fun unregisterClipboardListener() {
        if (isListenerRegistered) {
            try {
                clipboardManager.removePrimaryClipChangedListener(clipboardListener)
                isListenerRegistered = false
            } catch (e: Exception) {
                Log.w(TAG, "Failed to unregister clipboard listener", e)
            }
        }
        pollingJob?.cancel()
    }

    private fun startPollingFallback() {
        pollingJob?.cancel()
        pollingJob = serviceScope.launch {
            while (isActive) {
                delay(POLLING_INTERVAL_MS)
                withContext(Dispatchers.Main) {
                    checkClipboardForChanges()
                }
            }
        }
    }

    private fun checkClipboardForChanges() {
        if (!credentialStorage.syncEnabled) return

        try {
            val currentContent = getClipboardText()
            if (currentContent != null && currentContent != lastClipboardContent && currentContent != lastReceivedContent) {
                val hash = computeHash(currentContent)
                if (hash != lastContentHash) {
                    lastClipboardContent = currentContent
                    lastContentHash = hash
                    onNewClipboardContent(currentContent)
                }
            }
        } catch (e: Exception) {
            Log.w(TAG, "Error checking clipboard", e)
        }
    }

    private fun computeHash(content: String): String {
        return content.hashCode().toString()
    }

    private fun onClipboardChanged() {
        if (!credentialStorage.syncEnabled) return

        try {
            val content = getClipboardText()
            if (content != null && content != lastClipboardContent && content != lastReceivedContent) {
                val hash = computeHash(content)
                if (hash != lastContentHash) {
                    lastClipboardContent = content
                    lastContentHash = hash
                    onNewClipboardContent(content)
                }
            }
        } catch (e: Exception) {
            Log.w(TAG, "Error handling clipboard change", e)
        }
    }

    private fun getClipboardText(): String? {
        return try {
            val clip = clipboardManager.primaryClip
            if (clip != null && clip.description.hasMimeType(ClipDescription.MIMETYPE_TEXT_PLAIN)) {
                clip.getItemAt(0)?.text?.toString()
            } else {
                null
            }
        } catch (e: Exception) {
            Log.w(TAG, "Error getting clipboard text", e)
            null
        }
    }

    private fun onNewClipboardContent(content: String) {
        Log.d(TAG, "New clipboard content: ${content.take(50)}...")

        serviceScope.launch {
            try {
                client?.let { c ->
                    if (c.connectionState.value == ConnectionState.Authenticated) {
                        c.pushClipboard(content)
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Failed to push clipboard", e)
            }
        }
    }

    private suspend fun connectAndMaintain() {
        while (currentCoroutineContext().isActive) {
            if (!credentialStorage.syncEnabled) {
                updateStatus("Sync disabled")
                delay(RECONNECT_DELAY_MS)
                continue
            }

            if (!credentialStorage.hasCredentials) {
                updateStatus("Not configured")
                delay(RECONNECT_DELAY_MS)
                continue
            }

            val host = credentialStorage.serverHost!!
            val port = credentialStorage.serverPort
            val apiKey = credentialStorage.apiKey!!
            val deviceId = credentialStorage.deviceId
            val deviceName = credentialStorage.deviceName

            // Create new client
            client?.shutdown()
            val newClient = KryptonClient(deviceId, deviceName)
            client = newClient

            // Collect events in separate coroutine
            val eventJob = serviceScope.launch {
                newClient.events.collectLatest { event ->
                    handleEvent(event)
                }
            }

            // Collect connection state
            val stateJob = serviceScope.launch {
                newClient.connectionState.collectLatest { state ->
                    updateStatusForState(state)
                }
            }

            try {
                updateStatus("Connecting to $host...")
                if (newClient.connect(host, port)) {
                    updateStatus("Authenticating...")
                    newClient.authenticateWithApiKey(apiKey)

                    // Wait for disconnect or error
                    newClient.connectionState.collectLatest { state ->
                        if (state == ConnectionState.Disconnected || state is ConnectionState.Error) {
                            throw Exception("Connection lost")
                        }
                    }
                } else {
                    updateStatus("Connection failed")
                }
            } catch (e: Exception) {
                Log.e(TAG, "Connection error", e)
                updateStatus("Disconnected - retrying...")
            } finally {
                eventJob.cancel()
                stateJob.cancel()
            }

            delay(RECONNECT_DELAY_MS)
        }
    }

    private suspend fun handleEvent(event: KryptonEvent) {
        when (event) {
            is KryptonEvent.ClipboardReceived -> {
                handleClipboardBroadcast(event)
            }
            is KryptonEvent.AuthResult -> {
                if (!event.success) {
                    updateStatus("Auth failed: ${event.message}")
                }
            }
            is KryptonEvent.ConnectionError -> {
                updateStatus("Error: ${event.message}")
            }
            is KryptonEvent.HeartbeatLatency -> {
                synchronized(heartbeatLatencies) {
                    heartbeatLatencies.add(event.latencyMs)
                    while (heartbeatLatencies.size > MAX_HEARTBEAT_SAMPLES) {
                        heartbeatLatencies.removeAt(0)
                    }
                }
                // Update notification with new latency info
                updateNotification()
            }
        }
    }

    private suspend fun handleClipboardBroadcast(event: KryptonEvent.ClipboardReceived) {
        if (!credentialStorage.syncEnabled) return

        val entry = event.entry
        if (entry.contentType != ClipboardContentType.TEXT) {
            Log.d(TAG, "Ignoring non-text clipboard entry")
            return
        }

        val content = entry.content.toString(Charsets.UTF_8)
        Log.i(TAG, "Received clipboard from ${event.fromDevice}: ${content.take(50)}...")

        // Store received content to avoid echo
        lastReceivedContent = content
        lastClipboardContent = content
        lastContentHash = computeHash(content)

        // Set clipboard on main thread
        withContext(Dispatchers.Main) {
            try {
                val clip = ClipData.newPlainText("Krypton Sync", content)
                clipboardManager.setPrimaryClip(clip)
                Log.d(TAG, "Clipboard updated")
            } catch (e: Exception) {
                Log.e(TAG, "Failed to set clipboard", e)
            }
        }
    }

    private fun updateStatusForState(state: ConnectionState) {
        val message = when (state) {
            is ConnectionState.Disconnected -> "Disconnected"
            is ConnectionState.Connecting -> "Connecting..."
            is ConnectionState.Connected -> "Connected"
            is ConnectionState.Authenticating -> "Authenticating..."
            is ConnectionState.Authenticated -> "Syncing"
            is ConnectionState.Error -> "Error"
        }
        updateStatus(message)
    }

    private fun updateStatus(status: String) {
        currentStatus = status
        updateNotification()
    }

    private fun updateNotification() {
        val notificationManager = getSystemService(NOTIFICATION_SERVICE) as NotificationManager

        if (credentialStorage.showNotification) {
            val notification = createNotification()
            notificationManager.notify(NOTIFICATION_ID, notification)
        } else {
            // When notification is disabled, show minimal required notification for foreground service
            val notification = createMinimalNotification()
            notificationManager.notify(NOTIFICATION_ID, notification)
        }
    }

    private fun createNotificationChannels() {
        val notificationManager = getSystemService(NOTIFICATION_SERVICE) as NotificationManager

        // Main channel with low importance (no sound/vibration by default)
        val channel = NotificationChannel(
            CHANNEL_ID,
            "Krypton Clipboard Sync",
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = "Shows clipboard sync status"
            setShowBadge(false)
            enableLights(false)
            enableVibration(false)
            setSound(null, null)
        }
        notificationManager.createNotificationChannel(channel)
    }

    private fun createNotification(): Notification {
        val pendingIntent = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )

        val stopIntent = PendingIntent.getService(
            this,
            1,
            Intent(this, ClipboardSyncService::class.java).apply { action = ACTION_STOP },
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )

        // Build detailed status text
        val monitoringTypeStr = when (currentMonitoringType) {
            ClipboardMonitoringType.LISTENER -> "Listener"
            ClipboardMonitoringType.POLLING -> "Polling"
            ClipboardMonitoringType.LOGCAT_OVERLAY -> "Logcat+Overlay"
        }

        val latencyStr = if (heartbeatLatencies.isNotEmpty()) {
            val avg = getAverageLatency()
            "${avg}ms"
        } else {
            "N/A"
        }

        val contentText = "$currentStatus | $monitoringTypeStr | Ping: $latencyStr"

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("Krypton Sync")
            .setContentText(contentText)
            .setSmallIcon(R.drawable.ic_menu_share)
            .setOngoing(true)
            .setContentIntent(pendingIntent)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .addAction(R.drawable.ic_menu_close_clear_cancel, "Stop", stopIntent)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .build()
    }

    private fun createMinimalNotification(): Notification {
        val pendingIntent = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )

        // Minimal notification - required for foreground service but hidden
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("")
            .setContentText("")
            .setSmallIcon(R.drawable.ic_menu_share)
            .setOngoing(true)
            .setContentIntent(pendingIntent)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .setPriority(NotificationCompat.PRIORITY_MIN)
            .setVisibility(NotificationCompat.VISIBILITY_SECRET)
            .build()
    }
}
