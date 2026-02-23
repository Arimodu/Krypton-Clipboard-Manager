package dev.arimodu.krypton.ui.screens

import android.Manifest
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
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

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SetupScreen(
    credentialStorage: CredentialStorage,
    onSetupComplete: () -> Unit
) {
    val context = LocalContext.current

    var batteryOptimizationDisabled by remember {
        mutableStateOf(isBatteryOptimizationDisabled(context))
    }
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
    var overlayPermissionGranted by remember {
        mutableStateOf(Settings.canDrawOverlays(context))
    }

    val notificationPermissionLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestPermission()
    ) { isGranted ->
        notificationPermissionGranted = isGranted
    }

    // Refresh states when returning to the app
    LaunchedEffect(Unit) {
        // This will be triggered when the composable is first displayed
    }

    // Check permissions periodically when screen is visible
    DisposableEffect(Unit) {
        onDispose { }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Initial Setup") }
            )
        }
    ) { paddingValues ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
                .padding(16.dp)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Text(
                text = "Welcome to Krypton Sync",
                style = MaterialTheme.typography.headlineMedium
            )

            Text(
                text = "To keep your clipboard synced in the background, we need a few permissions. " +
                        "Please follow these steps:",
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Spacer(modifier = Modifier.height(8.dp))

            // Step 1: Battery Optimization
            SetupStepCard(
                stepNumber = 1,
                title = "Disable Battery Optimization",
                description = "This allows the app to run in the background without being killed by the system.",
                isCompleted = batteryOptimizationDisabled,
                onActionClick = {
                    openBatteryOptimizationSettings(context)
                },
                onRefresh = {
                    batteryOptimizationDisabled = isBatteryOptimizationDisabled(context)
                }
            )

            // Step 2: Autostart Permission (manufacturer-specific)
            SetupStepCard(
                stepNumber = 2,
                title = "Enable Autostart",
                description = "On some phones (Xiaomi, Huawei, OPPO, etc.), you need to enable autostart " +
                        "to allow the app to start when your phone boots up.",
                isCompleted = null, // Can't detect this programmatically
                onActionClick = {
                    openAutostartSettings(context)
                },
                onRefresh = null,
                actionText = "Open Settings"
            )

            // Step 3: Overlay Permission (Android 10+)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                SetupStepCard(
                    stepNumber = 3,
                    title = "Display Over Other Apps",
                    description = "Required for background clipboard access on Android 10+. " +
                            "This allows the app to briefly gain focus to read the clipboard.",
                    isCompleted = overlayPermissionGranted,
                    onActionClick = {
                        openOverlaySettings(context)
                    },
                    onRefresh = {
                        overlayPermissionGranted = Settings.canDrawOverlays(context)
                    }
                )
            }

            // Step 4: Notification Permission (Android 13+)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                val stepNum = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) 4 else 3
                SetupStepCard(
                    stepNumber = stepNum,
                    title = "Notification Permission",
                    description = "Required to show the sync status notification (can be disabled later).",
                    isCompleted = notificationPermissionGranted,
                    onActionClick = {
                        notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                    },
                    onRefresh = {
                        notificationPermissionGranted = ContextCompat.checkSelfPermission(
                            context,
                            Manifest.permission.POST_NOTIFICATIONS
                        ) == PackageManager.PERMISSION_GRANTED
                    }
                )
            }

            // Important note about Android 10+ clipboard restrictions
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer
                    )
                ) {
                    Row(
                        modifier = Modifier.padding(16.dp),
                        horizontalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        Icon(
                            Icons.Default.Warning,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.error
                        )
                        Column {
                            Text(
                                text = "Important: Android Clipboard Restrictions",
                                style = MaterialTheme.typography.titleSmall,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onErrorContainer
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = "Android 10 and later restrict background clipboard access. " +
                                        "For full functionality, additional setup via ADB is required. " +
                                        "You can do this later from the app settings.",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onErrorContainer
                            )
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.weight(1f))

            // Continue button
            Button(
                onClick = {
                    credentialStorage.setupCompleted = true
                    onSetupComplete()
                },
                modifier = Modifier.fillMaxWidth(),
                enabled = batteryOptimizationDisabled &&
                        (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU || notificationPermissionGranted)
            ) {
                Text("Continue to Login")
            }

            // Skip button
            TextButton(
                onClick = {
                    credentialStorage.setupCompleted = true
                    onSetupComplete()
                },
                modifier = Modifier.fillMaxWidth()
            ) {
                Text("Skip for now")
            }
        }
    }
}

@Composable
private fun SetupStepCard(
    stepNumber: Int,
    title: String,
    description: String,
    isCompleted: Boolean?,
    onActionClick: () -> Unit,
    onRefresh: (() -> Unit)?,
    actionText: String = "Grant Permission"
) {
    Card(
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                // Step number or checkmark
                Surface(
                    shape = MaterialTheme.shapes.small,
                    color = when (isCompleted) {
                        true -> MaterialTheme.colorScheme.primary
                        false -> MaterialTheme.colorScheme.surfaceVariant
                        null -> MaterialTheme.colorScheme.surfaceVariant
                    }
                ) {
                    Box(
                        modifier = Modifier.size(32.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        if (isCompleted == true) {
                            Icon(
                                Icons.Default.Check,
                                contentDescription = "Completed",
                                tint = MaterialTheme.colorScheme.onPrimary,
                                modifier = Modifier.size(20.dp)
                            )
                        } else {
                            Text(
                                text = stepNumber.toString(),
                                style = MaterialTheme.typography.titleMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }

                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = title,
                        style = MaterialTheme.typography.titleMedium
                    )
                    if (isCompleted == true) {
                        Text(
                            text = "Completed",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.primary
                        )
                    }
                }
            }

            Text(
                text = description,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            if (isCompleted != true) {
                Row(
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    Button(
                        onClick = onActionClick,
                        modifier = Modifier.weight(1f)
                    ) {
                        Text(actionText)
                    }

                    if (onRefresh != null) {
                        OutlinedButton(onClick = onRefresh) {
                            Text("Check")
                        }
                    }
                }
            }
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
        // Fallback to general battery settings
        try {
            val intent = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
            context.startActivity(intent)
        } catch (e2: Exception) {
            // Last resort: open app settings
            openAppSettings(context)
        }
    }
}

private fun openAutostartSettings(context: Context) {
    // Try manufacturer-specific autostart settings
    val autostartIntents = listOf(
        // Xiaomi
        Intent().setComponent(ComponentName("com.miui.securitycenter", "com.miui.permcenter.autostart.AutoStartManagementActivity")),
        // Huawei
        Intent().setComponent(ComponentName("com.huawei.systemmanager", "com.huawei.systemmanager.startupmgr.ui.StartupNormalAppListActivity")),
        Intent().setComponent(ComponentName("com.huawei.systemmanager", "com.huawei.systemmanager.optimize.process.ProtectActivity")),
        // OPPO
        Intent().setComponent(ComponentName("com.coloros.safecenter", "com.coloros.safecenter.permission.startup.StartupAppListActivity")),
        Intent().setComponent(ComponentName("com.oppo.safe", "com.oppo.safe.permission.startup.StartupAppListActivity")),
        // Vivo
        Intent().setComponent(ComponentName("com.vivo.permissionmanager", "com.vivo.permissionmanager.activity.BgStartUpManagerActivity")),
        Intent().setComponent(ComponentName("com.iqoo.secure", "com.iqoo.secure.ui.phoneoptimize.AddWhiteListActivity")),
        // Samsung
        Intent().setComponent(ComponentName("com.samsung.android.lool", "com.samsung.android.sm.ui.battery.BatteryActivity")),
        // OnePlus
        Intent().setComponent(ComponentName("com.oneplus.security", "com.oneplus.security.chainlaunch.view.ChainLaunchAppListActivity")),
        // Asus
        Intent().setComponent(ComponentName("com.asus.mobilemanager", "com.asus.mobilemanager.autostart.AutoStartActivity")),
        // Letv
        Intent().setComponent(ComponentName("com.letv.android.letvsafe", "com.letv.android.letvsafe.AutobootManageActivity")),
    )

    for (intent in autostartIntents) {
        try {
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            if (context.packageManager.resolveActivity(intent, PackageManager.MATCH_DEFAULT_ONLY) != null) {
                context.startActivity(intent)
                return
            }
        } catch (e: Exception) {
            continue
        }
    }

    // Fallback: open app settings
    openAppSettings(context)
}

private fun openAppSettings(context: Context) {
    try {
        val intent = Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
            data = Uri.parse("package:${context.packageName}")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        context.startActivity(intent)
    } catch (e: Exception) {
        // Ignore
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
        // Fallback to general settings
        openAppSettings(context)
    }
}
