package dev.arimodu.krypton

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import dev.arimodu.krypton.data.CredentialStorage
import dev.arimodu.krypton.service.ClipboardSyncService
import dev.arimodu.krypton.ui.screens.AdbGuideScreen
import dev.arimodu.krypton.ui.screens.LoginScreen
import dev.arimodu.krypton.ui.screens.SetupScreen
import dev.arimodu.krypton.ui.screens.StatusScreen
import dev.arimodu.krypton.ui.theme.KryptonAndroidTheme

class MainActivity : ComponentActivity() {
    private lateinit var credentialStorage: CredentialStorage

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        credentialStorage = CredentialStorage(this)

        setContent {
            KryptonAndroidTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    KryptonApp(
                        credentialStorage = credentialStorage,
                        checkOverlayPermission = { checkAndRequestOverlayPermission() }
                    )
                }
            }
        }

        // Start service if credentials exist and sync is enabled
        if (credentialStorage.hasCredentials && credentialStorage.syncEnabled) {
            startSyncService()
        }
    }

    /**
     * Checks if overlay permission is needed and not granted.
     * Returns true if the permission settings was opened.
     */
    private fun checkAndRequestOverlayPermission(): Boolean {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && !Settings.canDrawOverlays(this)) {
            try {
                val intent = Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION).apply {
                    data = Uri.parse("package:$packageName")
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                startActivity(intent)
                return true
            } catch (e: Exception) {
                // Ignore
            }
        }
        return false
    }

    override fun onResume() {
        super.onResume()
        // When app comes to foreground, sync clipboard if it changed
        // This is especially useful when logcat monitoring isn't available
        ClipboardSyncService.getInstance()?.onAppForeground()
    }

    private fun startSyncService() {
        val intent = Intent(this, ClipboardSyncService::class.java).apply {
            action = ClipboardSyncService.ACTION_START
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(intent)
        } else {
            startService(intent)
        }
    }
}

sealed class Screen {
    data object Setup : Screen()
    data object Login : Screen()
    data object Status : Screen()
    data object AdbGuide : Screen()
}

@Composable
fun KryptonApp(
    credentialStorage: CredentialStorage,
    checkOverlayPermission: () -> Boolean
) {
    val context = androidx.compose.ui.platform.LocalContext.current
    var currentScreen by remember {
        mutableStateOf<Screen>(
            when {
                !credentialStorage.setupCompleted -> Screen.Setup
                !credentialStorage.hasCredentials -> Screen.Login
                else -> Screen.Status
            }
        )
    }

    // Track if we've shown the overlay permission dialog this session
    var overlayDialogShown by remember { mutableStateOf(false) }
    var showOverlayDialog by remember { mutableStateOf(false) }

    // Check for overlay permission when entering Status screen on Android 10+
    LaunchedEffect(currentScreen) {
        if (currentScreen == Screen.Status && !overlayDialogShown) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q &&
                !Settings.canDrawOverlays(context)) {
                showOverlayDialog = true
                overlayDialogShown = true
            }
        }
    }

    // Overlay permission dialog
    if (showOverlayDialog) {
        AlertDialog(
            onDismissRequest = { showOverlayDialog = false },
            title = { Text("Overlay Permission Required") },
            text = {
                Text(
                    "On Android 10+, Krypton needs the 'Display over other apps' permission " +
                    "to read the clipboard in the background.\n\n" +
                    "Without this permission, clipboard sync will only work when the app is open."
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        showOverlayDialog = false
                        checkOverlayPermission()
                    }
                ) {
                    Text("Grant Permission")
                }
            },
            dismissButton = {
                TextButton(onClick = { showOverlayDialog = false }) {
                    Text("Later")
                }
            }
        )
    }

    when (currentScreen) {
        is Screen.Setup -> {
            SetupScreen(
                credentialStorage = credentialStorage,
                onSetupComplete = {
                    currentScreen = if (credentialStorage.hasCredentials) {
                        Screen.Status
                    } else {
                        Screen.Login
                    }
                }
            )
        }

        is Screen.Login -> {
            LoginScreen(
                credentialStorage = credentialStorage,
                onLoginSuccess = {
                    currentScreen = Screen.Status
                }
            )
        }

        is Screen.Status -> {
            StatusScreen(
                credentialStorage = credentialStorage,
                onLogout = {
                    currentScreen = Screen.Login
                },
                onOpenAdbGuide = {
                    currentScreen = Screen.AdbGuide
                }
            )
        }

        is Screen.AdbGuide -> {
            AdbGuideScreen(
                credentialStorage = credentialStorage,
                onBack = {
                    currentScreen = Screen.Status
                }
            )
        }
    }
}
