package dev.arimodu.krypton.ui.screens

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import dev.arimodu.krypton.data.CredentialStorage
import dev.arimodu.krypton.service.ClipboardSyncService

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AdbGuideScreen(
    credentialStorage: CredentialStorage,
    onBack: () -> Unit
) {
    val context = LocalContext.current
    val packageName = context.packageName
    val readClipboardCommand = "adb shell appops set $packageName READ_CLIPBOARD allow"
    val readLogsCommand = "adb shell pm grant $packageName android.permission.READ_LOGS"

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Background Clipboard Access") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
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
            // Warning card
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
                            text = "Why is this needed?",
                            style = MaterialTheme.typography.titleSmall,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.onErrorContainer
                        )
                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "Starting with Android 10, apps cannot read the clipboard in the background. " +
                                    "This means when you copy something on your phone, Krypton cannot detect it " +
                                    "unless the app is open on screen.\n\n" +
                                    "To enable full clipboard sync, you need to grant a special permission using ADB " +
                                    "(Android Debug Bridge) on a computer.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onErrorContainer
                        )
                    }
                }
            }

            Text(
                text = "Step-by-Step Guide",
                style = MaterialTheme.typography.headlineSmall
            )

            // Step 1: Enable Developer Options
            GuideStepCard(
                stepNumber = 1,
                title = "Enable Developer Options on your phone",
                content = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("1. Open your phone's Settings app")
                        Text("2. Scroll down and tap \"About phone\" (or \"About device\")")
                        Text("3. Find \"Build number\" and tap it 7 times quickly")
                        Text("4. You'll see a message saying \"You are now a developer!\"")

                        Spacer(modifier = Modifier.height(8.dp))

                        OutlinedButton(
                            onClick = {
                                try {
                                    val intent = Intent(Settings.ACTION_DEVICE_INFO_SETTINGS)
                                    context.startActivity(intent)
                                } catch (e: Exception) {
                                    try {
                                        val intent = Intent(Settings.ACTION_SETTINGS)
                                        context.startActivity(intent)
                                    } catch (e2: Exception) {
                                        // Ignore
                                    }
                                }
                            }
                        ) {
                            Text("Open About Phone")
                        }
                    }
                }
            )

            // Step 2: Enable USB Debugging
            GuideStepCard(
                stepNumber = 2,
                title = "Enable USB Debugging",
                content = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("1. Go back to Settings")
                        Text("2. Tap \"System\" (or scroll to find \"Developer options\")")
                        Text("3. Tap \"Developer options\"")
                        Text("4. Find \"USB debugging\" and turn it ON")
                        Text("5. Confirm when prompted")

                        Spacer(modifier = Modifier.height(8.dp))

                        OutlinedButton(
                            onClick = {
                                try {
                                    val intent = Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS)
                                    context.startActivity(intent)
                                } catch (e: Exception) {
                                    // Ignore
                                }
                            }
                        ) {
                            Text("Open Developer Options")
                        }
                    }
                }
            )

            // Step 3: Install ADB on Computer
            GuideStepCard(
                stepNumber = 3,
                title = "Install ADB on your computer",
                content = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(
                            text = "Windows:",
                            fontWeight = FontWeight.Bold
                        )
                        Text("1. Download \"platform-tools\" from Google")
                        Text("2. Extract the ZIP file to a folder (e.g., C:\\adb)")

                        Spacer(modifier = Modifier.height(4.dp))

                        Text(
                            text = "Mac:",
                            fontWeight = FontWeight.Bold
                        )
                        Text("1. Open Terminal")
                        Text("2. Run: brew install android-platform-tools")
                        Text("   (Install Homebrew first if you don't have it)")

                        Spacer(modifier = Modifier.height(4.dp))

                        Text(
                            text = "Linux:",
                            fontWeight = FontWeight.Bold
                        )
                        Text("1. Open Terminal")
                        Text("2. Run: sudo apt install adb")

                        Spacer(modifier = Modifier.height(8.dp))

                        OutlinedButton(
                            onClick = {
                                val intent = Intent(Intent.ACTION_VIEW).apply {
                                    data = Uri.parse("https://developer.android.com/tools/releases/platform-tools")
                                }
                                context.startActivity(intent)
                            }
                        ) {
                            Text("Download Platform Tools")
                        }
                    }
                }
            )

            // Step 4: Connect Phone
            GuideStepCard(
                stepNumber = 4,
                title = "Connect your phone to the computer",
                content = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("1. Connect your phone using a USB cable")
                        Text("2. A popup will appear on your phone asking to allow USB debugging")
                        Text("3. Check \"Always allow from this computer\"")
                        Text("4. Tap \"Allow\"")

                        Spacer(modifier = Modifier.height(8.dp))

                        Card(
                            colors = CardDefaults.cardColors(
                                containerColor = MaterialTheme.colorScheme.surfaceVariant
                            )
                        ) {
                            Text(
                                text = "Tip: If the popup doesn't appear, try unplugging and replugging the USB cable, " +
                                        "or try a different USB port.",
                                modifier = Modifier.padding(12.dp),
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                    }
                }
            )

            // Step 5: Run the Commands
            GuideStepCard(
                stepNumber = 5,
                title = "Run the ADB commands",
                content = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(
                            text = "Windows:",
                            fontWeight = FontWeight.Bold
                        )
                        Text("1. Open Command Prompt (search \"cmd\" in Start menu)")
                        Text("2. Navigate to the folder where you extracted ADB:")
                        CodeBlock(text = "cd C:\\adb\\platform-tools", context = context)
                        Text("3. Run these commands:")

                        Spacer(modifier = Modifier.height(4.dp))

                        Text(
                            text = "Mac / Linux:",
                            fontWeight = FontWeight.Bold
                        )
                        Text("1. Open Terminal")
                        Text("2. Run these commands:")

                        Spacer(modifier = Modifier.height(8.dp))

                        // Command 1: READ_CLIPBOARD
                        Card(
                            modifier = Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(
                                containerColor = MaterialTheme.colorScheme.primaryContainer
                            )
                        ) {
                            Column(modifier = Modifier.padding(12.dp)) {
                                Text(
                                    text = "Command 1 - Enable clipboard access:",
                                    style = MaterialTheme.typography.labelMedium,
                                    color = MaterialTheme.colorScheme.onPrimaryContainer
                                )
                                Spacer(modifier = Modifier.height(8.dp))
                                CodeBlock(
                                    text = readClipboardCommand,
                                    context = context,
                                    highlight = true
                                )
                            }
                        }

                        Spacer(modifier = Modifier.height(8.dp))

                        // Command 2: READ_LOGS
                        Card(
                            modifier = Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(
                                containerColor = MaterialTheme.colorScheme.primaryContainer
                            )
                        ) {
                            Column(modifier = Modifier.padding(12.dp)) {
                                Text(
                                    text = "Command 2 - Enable log monitoring (REQUIRED via ADB):",
                                    style = MaterialTheme.typography.labelMedium,
                                    color = MaterialTheme.colorScheme.onPrimaryContainer
                                )
                                Text(
                                    text = "Note: This permission MUST be granted via ADB. " +
                                            "The popup that appears when you start the app only grants temporary access " +
                                            "that resets when the app closes.",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.8f)
                                )
                                Spacer(modifier = Modifier.height(8.dp))
                                CodeBlock(
                                    text = readLogsCommand,
                                    context = context,
                                    highlight = true
                                )
                            }
                        }

                        Spacer(modifier = Modifier.height(8.dp))

                        Text("4. Press Enter after each command")
                        Text("5. If successful, you'll see no error message")
                    }
                }
            )

            // Step 6: Verify
            GuideStepCard(
                stepNumber = 6,
                title = "Verify it worked",
                content = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("1. Disconnect your phone from the computer")
                        Text("2. Copy some text on another device connected to Krypton")
                        Text("3. Check if it appears on this phone!")

                        Spacer(modifier = Modifier.height(8.dp))

                        Text(
                            text = "Note: You only need to do this once. The permission will persist " +
                                    "until you uninstall the app or reset your phone.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            )

            // Acknowledge button
            Spacer(modifier = Modifier.height(16.dp))

            Button(
                onClick = {
                    credentialStorage.adbPermissionAcknowledged = true
                    onBack()
                },
                modifier = Modifier.fillMaxWidth()
            ) {
                Text("I've completed these steps")
            }

            TextButton(
                onClick = onBack,
                modifier = Modifier.fillMaxWidth()
            ) {
                Text("I'll do this later")
            }

            Spacer(modifier = Modifier.height(32.dp))
        }
    }
}

@Composable
private fun GuideStepCard(
    stepNumber: Int,
    title: String,
    content: @Composable () -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Surface(
                    shape = RoundedCornerShape(8.dp),
                    color = MaterialTheme.colorScheme.primary
                ) {
                    Box(
                        modifier = Modifier.size(36.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = stepNumber.toString(),
                            style = MaterialTheme.typography.titleMedium,
                            color = MaterialTheme.colorScheme.onPrimary,
                            fontWeight = FontWeight.Bold
                        )
                    }
                }

                Text(
                    text = title,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold
                )
            }

            content()
        }
    }
}

@Composable
private fun CodeBlock(
    text: String,
    context: Context,
    highlight: Boolean = false
) {
    val clipboardManager = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                color = if (highlight)
                    MaterialTheme.colorScheme.inverseSurface.copy(alpha = 0.1f)
                else
                    MaterialTheme.colorScheme.surfaceVariant,
                shape = RoundedCornerShape(8.dp)
            )
            .padding(12.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = text,
            fontFamily = FontFamily.Monospace,
            fontSize = 13.sp,
            modifier = Modifier
                .weight(1f)
                .horizontalScroll(rememberScrollState()),
            color = if (highlight) MaterialTheme.colorScheme.onPrimaryContainer else MaterialTheme.colorScheme.onSurfaceVariant
        )

        IconButton(
            onClick = {
                val clip = ClipData.newPlainText("ADB Command", text)
                clipboardManager.setPrimaryClip(clip)

                // Also push to server so PC can paste
                ClipboardSyncService.getInstance()?.pushClipboardFromForeground(text)

                Toast.makeText(context, "Copied to clipboard (syncing to server...)", Toast.LENGTH_SHORT).show()
            }
        ) {
            Icon(
                Icons.Default.ContentCopy,
                contentDescription = "Copy",
                modifier = Modifier.size(20.dp)
            )
        }
    }
}
