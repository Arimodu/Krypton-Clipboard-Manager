package dev.arimodu.krypton.ui.screens

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.expandVertically
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import dev.arimodu.krypton.data.CredentialStorage
import dev.arimodu.krypton.service.ClipboardMonitoringType
import dev.arimodu.krypton.service.ClipboardSyncService
import kotlinx.coroutines.delay

data class PermissionStatus(
    val name: String,
    val description: String,
    val isGranted: Boolean,
    val isRequired: Boolean,
    val onFix: () -> Unit
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StatusScreen(
    credentialStorage: CredentialStorage,
    onLogout: () -> Unit,
    onOpenAdbGuide: () -> Unit
) {
    val context = LocalContext.current
    var isServiceRunning by remember { mutableStateOf(ClipboardSyncService.isRunning()) }
    var syncEnabled by remember { mutableStateOf(credentialStorage.syncEnabled) }
    var showNotification by remember { mutableStateOf(credentialStorage.showNotification) }
    var showLogoutDialog by remember { mutableStateOf(false) }

    // State from service
    var currentStatus by remember { mutableStateOf("Unknown") }
    var monitoringType by remember { mutableStateOf<ClipboardMonitoringType?>(null) }
    var heartbeatLatencies by remember { mutableStateOf<List<Long>>(emptyList()) }

    // Collapsible sections
    var tipsExpanded by remember { mutableStateOf(false) }
    var controlsExpanded by remember { mutableStateOf(true) }

    // Permission states
    var batteryOptimizationDisabled by remember { mutableStateOf(isBatteryOptimizationDisabled(context)) }
    var overlayPermissionGranted by remember { mutableStateOf(Settings.canDrawOverlays(context)) }
    var notificationPermissionGranted by remember {
        mutableStateOf(
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                ContextCompat.checkSelfPermission(
                    context,
                    Manifest.permission.POST_NOTIFICATIONS
                ) == PackageManager.PERMISSION_GRANTED
            } else true
        )
    }

    val notificationPermissionLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestPermission()
    ) { isGranted ->
        notificationPermissionGranted = isGranted
        if (isGranted && syncEnabled) {
            startService(context)
            isServiceRunning = true
        }
    }

    // Update service status periodically
    LaunchedEffect(Unit) {
        while (true) {
            isServiceRunning = ClipboardSyncService.isRunning()
            ClipboardSyncService.getInstance()?.let { service ->
                currentStatus = service.getStatus()
                monitoringType = service.getMonitoringType()
                heartbeatLatencies = service.getHeartbeatLatencies()
            }
            // Also refresh permission states
            batteryOptimizationDisabled = isBatteryOptimizationDisabled(context)
            overlayPermissionGranted = Settings.canDrawOverlays(context)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                notificationPermissionGranted = ContextCompat.checkSelfPermission(
                    context,
                    Manifest.permission.POST_NOTIFICATIONS
                ) == PackageManager.PERMISSION_GRANTED
            }
            delay(1000)
        }
    }

    // Start service when sync enabled changes
    LaunchedEffect(syncEnabled) {
        if (syncEnabled && !isServiceRunning) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                val hasPermission = ContextCompat.checkSelfPermission(
                    context,
                    Manifest.permission.POST_NOTIFICATIONS
                ) == PackageManager.PERMISSION_GRANTED

                if (!hasPermission && credentialStorage.showNotification) {
                    notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                    return@LaunchedEffect
                }
            }
            startService(context)
            isServiceRunning = true
        } else if (!syncEnabled && isServiceRunning) {
            stopService(context)
            isServiceRunning = false
        }
    }

    if (showLogoutDialog) {
        AlertDialog(
            onDismissRequest = { showLogoutDialog = false },
            title = { Text("Logout") },
            text = { Text("This will remove your saved credentials and stop the sync service. You'll need to log in again to continue syncing.") },
            confirmButton = {
                TextButton(
                    onClick = {
                        showLogoutDialog = false
                        stopService(context)
                        credentialStorage.clearAll()
                        onLogout()
                    }
                ) {
                    Text("Logout")
                }
            },
            dismissButton = {
                TextButton(onClick = { showLogoutDialog = false }) {
                    Text("Cancel")
                }
            }
        )
    }

    // Build permission statuses for Android 10+
    val missingPermissions = remember(batteryOptimizationDisabled, overlayPermissionGranted, notificationPermissionGranted) {
        buildList {
            if (!batteryOptimizationDisabled) {
                add(PermissionStatus(
                    name = "Battery Optimization",
                    description = "Required for reliable background operation",
                    isGranted = false,
                    isRequired = true,
                    onFix = { openBatteryOptimizationSettings(context) }
                ))
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && !overlayPermissionGranted) {
                add(PermissionStatus(
                    name = "Display Over Apps",
                    description = "Required for clipboard access on Android 10+",
                    isGranted = false,
                    isRequired = true,
                    onFix = { openOverlaySettings(context) }
                ))
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU && !notificationPermissionGranted && showNotification) {
                add(PermissionStatus(
                    name = "Notifications",
                    description = "Required to show sync status",
                    isGranted = false,
                    isRequired = false,
                    onFix = { notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS) }
                ))
            }
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Krypton Sync") },
                actions = {
                    IconButton(onClick = { showLogoutDialog = true }) {
                        Icon(Icons.AutoMirrored.Filled.Logout, contentDescription = "Logout")
                    }
                }
            )
        }
    ) { paddingValues ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // Permission warnings
            if (missingPermissions.isNotEmpty()) {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer
                    )
                ) {
                    Column(
                        modifier = Modifier.padding(16.dp),
                        verticalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Icon(
                                Icons.Default.Warning,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.error
                            )
                            Text(
                                text = "Missing Permissions",
                                style = MaterialTheme.typography.titleSmall,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onErrorContainer
                            )
                        }

                        missingPermissions.forEach { permission ->
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clickable { permission.onFix() },
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Column(modifier = Modifier.weight(1f)) {
                                    Text(
                                        text = permission.name,
                                        style = MaterialTheme.typography.bodyMedium,
                                        fontWeight = FontWeight.Medium,
                                        color = MaterialTheme.colorScheme.onErrorContainer
                                    )
                                    Text(
                                        text = permission.description,
                                        style = MaterialTheme.typography.bodySmall,
                                        color = MaterialTheme.colorScheme.onErrorContainer.copy(alpha = 0.8f)
                                    )
                                }
                                TextButton(onClick = permission.onFix) {
                                    Text("Fix")
                                }
                            }
                        }
                    }
                }
            }

            // Status card with detailed info
            Card(
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    Text(
                        text = "Connection Status",
                        style = MaterialTheme.typography.titleMedium
                    )

                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        val (statusText, statusColor) = if (isServiceRunning) {
                            currentStatus to MaterialTheme.colorScheme.primary
                        } else {
                            "Stopped" to MaterialTheme.colorScheme.error
                        }

                        Surface(
                            shape = MaterialTheme.shapes.small,
                            color = statusColor.copy(alpha = 0.2f)
                        ) {
                            Text(
                                text = statusText,
                                modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
                                color = statusColor,
                                style = MaterialTheme.typography.bodyMedium
                            )
                        }
                    }

                    HorizontalDivider()

                    // Server info
                    Text(
                        text = "Server: ${credentialStorage.serverHost}:${credentialStorage.serverPort}",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )

                    Text(
                        text = "Device: ${credentialStorage.deviceName}",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )

                    // Monitoring type
                    if (isServiceRunning && monitoringType != null) {
                        val monitoringTypeStr = when (monitoringType) {
                            ClipboardMonitoringType.LISTENER -> "Event Listener"
                            ClipboardMonitoringType.POLLING -> "Polling (1s)"
                            ClipboardMonitoringType.LOGCAT_OVERLAY -> "Logcat + Overlay"
                            null -> "Unknown"
                        }
                        Text(
                            text = "Monitoring: $monitoringTypeStr",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )

                        // Show warning if on Android 10+ but not using optimal monitoring
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q &&
                            monitoringType != ClipboardMonitoringType.LOGCAT_OVERLAY) {
                            Spacer(modifier = Modifier.height(4.dp))
                            Surface(
                                shape = MaterialTheme.shapes.small,
                                color = MaterialTheme.colorScheme.errorContainer.copy(alpha = 0.5f)
                            ) {
                                Text(
                                    text = "Background sync limited - see Missing Permissions above",
                                    modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.error
                                )
                            }
                        }
                    }

                    // Heartbeat latencies
                    if (isServiceRunning && heartbeatLatencies.isNotEmpty()) {
                        HorizontalDivider()
                        Text(
                            text = "Heartbeat Latency",
                            style = MaterialTheme.typography.titleSmall
                        )
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            heartbeatLatencies.forEach { latency ->
                                Surface(
                                    shape = MaterialTheme.shapes.extraSmall,
                                    color = MaterialTheme.colorScheme.surfaceVariant
                                ) {
                                    Text(
                                        text = "${latency}ms",
                                        modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp),
                                        style = MaterialTheme.typography.labelSmall,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant
                                    )
                                }
                            }
                        }
                        val avgLatency = if (heartbeatLatencies.isNotEmpty()) {
                            heartbeatLatencies.average().toLong()
                        } else 0L
                        Text(
                            text = "Average: ${avgLatency}ms",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }

            // Sync toggle card
            Card(
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = "Clipboard Sync",
                            style = MaterialTheme.typography.titleMedium
                        )
                        Text(
                            text = if (syncEnabled) "Syncing clipboard across devices" else "Sync disabled",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                    Switch(
                        checked = syncEnabled,
                        onCheckedChange = {
                            syncEnabled = it
                            credentialStorage.syncEnabled = it
                        }
                    )
                }
            }

            // Notification toggle card
            Card(
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = "Show Notification",
                            style = MaterialTheme.typography.titleMedium
                        )
                        Text(
                            text = if (showNotification) "Status shown in notification" else "Notification hidden",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                    Switch(
                        checked = showNotification,
                        onCheckedChange = {
                            showNotification = it
                            credentialStorage.showNotification = it
                            // Restart service to apply notification change
                            if (isServiceRunning) {
                                stopService(context)
                                startService(context)
                            }
                        }
                    )
                }
            }

            // Collapsible Controls section
            Card(
                modifier = Modifier.fillMaxWidth()
            ) {
                Column {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { controlsExpanded = !controlsExpanded }
                            .padding(16.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = "Service Controls",
                            style = MaterialTheme.typography.titleMedium
                        )
                        Icon(
                            imageVector = if (controlsExpanded) Icons.Default.KeyboardArrowUp else Icons.Default.KeyboardArrowDown,
                            contentDescription = if (controlsExpanded) "Collapse" else "Expand"
                        )
                    }

                    AnimatedVisibility(
                        visible = controlsExpanded,
                        enter = expandVertically(),
                        exit = shrinkVertically()
                    ) {
                        Column(
                            modifier = Modifier.padding(start = 16.dp, end = 16.dp, bottom = 16.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            // Manual service control
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                OutlinedButton(
                                    onClick = {
                                        if (isServiceRunning) {
                                            stopService(context)
                                        } else {
                                            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                                                val hasPermission = ContextCompat.checkSelfPermission(
                                                    context,
                                                    Manifest.permission.POST_NOTIFICATIONS
                                                ) == PackageManager.PERMISSION_GRANTED

                                                if (!hasPermission && credentialStorage.showNotification) {
                                                    notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                                                    return@OutlinedButton
                                                }
                                            }
                                            startService(context)
                                        }
                                        isServiceRunning = !isServiceRunning
                                    },
                                    modifier = Modifier.weight(1f)
                                ) {
                                    Text(if (isServiceRunning) "Stop Service" else "Start Service")
                                }
                            }

                            // ADB Guide button (Android 10+)
                            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                                OutlinedButton(
                                    onClick = onOpenAdbGuide,
                                    modifier = Modifier.fillMaxWidth()
                                ) {
                                    Text("Background Clipboard Setup Guide")
                                }
                            }
                        }
                    }
                }
            }

            // ADB Permission warning for Android 10+
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && !credentialStorage.adbPermissionAcknowledged) {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.secondaryContainer
                    ),
                    onClick = onOpenAdbGuide
                ) {
                    Row(
                        modifier = Modifier.padding(16.dp),
                        horizontalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        Icon(
                            Icons.Default.Warning,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.secondary
                        )
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = "ADB Setup Recommended",
                                style = MaterialTheme.typography.titleSmall,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onSecondaryContainer
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = "For best clipboard access on Android 10+, ADB commands are recommended. Tap to see the guide.",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSecondaryContainer
                            )
                        }
                    }
                }
            }

            // Collapsible Tips section
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceVariant
                )
            ) {
                Column {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { tipsExpanded = !tipsExpanded }
                            .padding(16.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = "Tips & Information",
                            style = MaterialTheme.typography.titleMedium
                        )
                        Icon(
                            imageVector = if (tipsExpanded) Icons.Default.KeyboardArrowUp else Icons.Default.KeyboardArrowDown,
                            contentDescription = if (tipsExpanded) "Collapse" else "Expand"
                        )
                    }

                    AnimatedVisibility(
                        visible = tipsExpanded,
                        enter = expandVertically(),
                        exit = shrinkVertically()
                    ) {
                        Column(
                            modifier = Modifier.padding(start = 16.dp, end = 16.dp, bottom = 16.dp),
                            verticalArrangement = Arrangement.spacedBy(16.dp)
                        ) {
                            // Quick settings tile info
                            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                Text(
                                    text = "Quick Settings Tile",
                                    style = MaterialTheme.typography.titleSmall
                                )
                                Text(
                                    text = "You can add Krypton Sync to your Quick Settings panel for easy access. " +
                                            "Swipe down from the top of your screen, tap the edit (pencil) icon, " +
                                            "and drag 'Krypton Sync' to your active tiles.",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }

                            HorizontalDivider()

                            // How it works
                            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                Text(
                                    text = "How it works",
                                    style = MaterialTheme.typography.titleSmall
                                )
                                Text(
                                    text = "When you copy text on any connected device, it automatically " +
                                            "syncs to all your other devices. The service runs in the " +
                                            "background to keep everything in sync.",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }

                            HorizontalDivider()

                            // Monitoring types explanation
                            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                Text(
                                    text = "Monitoring Types",
                                    style = MaterialTheme.typography.titleSmall
                                )
                                Text(
                                    text = "• Event Listener: Best for Android 9 and below\n" +
                                            "• Logcat + Overlay: Required for Android 10+ (needs ADB setup)\n" +
                                            "• Polling: Fallback method, checks every second",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))
        }
    }
}

private fun isBatteryOptimizationDisabled(context: Context): Boolean {
    val powerManager = context.getSystemService(Context.POWER_SERVICE) as PowerManager
    return powerManager.isIgnoringBatteryOptimizations(context.packageName)
}

private fun openBatteryOptimizationSettings(context: Context) {
    try {
        val intent = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
            data = Uri.parse("package:${context.packageName}")
        }
        context.startActivity(intent)
    } catch (e: Exception) {
        try {
            val intent = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
            context.startActivity(intent)
        } catch (e2: Exception) {
            // Ignore
        }
    }
}

private fun openOverlaySettings(context: Context) {
    try {
        val intent = Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION).apply {
            data = Uri.parse("package:${context.packageName}")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        context.startActivity(intent)
    } catch (e: Exception) {
        // Ignore
    }
}

private fun startService(context: Context) {
    val intent = Intent(context, ClipboardSyncService::class.java).apply {
        action = ClipboardSyncService.ACTION_START
    }
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
        context.startForegroundService(intent)
    } else {
        context.startService(intent)
    }
}

private fun stopService(context: Context) {
    val intent = Intent(context, ClipboardSyncService::class.java).apply {
        action = ClipboardSyncService.ACTION_STOP
    }
    context.startService(intent)
}
