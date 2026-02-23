package dev.arimodu.krypton.ui.screens

import android.os.Build
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import dev.arimodu.krypton.data.CredentialStorage
import dev.arimodu.krypton.network.KryptonClient
import dev.arimodu.krypton.network.KryptonEvent
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(
    credentialStorage: CredentialStorage,
    onLoginSuccess: () -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()

    var serverHost by remember { mutableStateOf(credentialStorage.serverHost ?: "") }
    var serverPort by remember { mutableStateOf(credentialStorage.serverPort.toString()) }
    var username by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }

    var isLoading by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var statusMessage by remember { mutableStateOf<String?>(null) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Krypton Setup") }
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
                text = "Connect to Krypton Server",
                style = MaterialTheme.typography.headlineSmall
            )

            Text(
                text = "Enter your server details and credentials to start syncing your clipboard.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Spacer(modifier = Modifier.height(8.dp))

            // Server section
            Text(
                text = "Server",
                style = MaterialTheme.typography.titleMedium
            )

            OutlinedTextField(
                value = serverHost,
                onValueChange = { serverHost = it },
                label = { Text("Server Address") },
                placeholder = { Text("192.168.1.100 or krypton.example.com") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                enabled = !isLoading,
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Uri,
                    imeAction = ImeAction.Next
                )
            )

            OutlinedTextField(
                value = serverPort,
                onValueChange = { serverPort = it.filter { c -> c.isDigit() } },
                label = { Text("Port") },
                placeholder = { Text("6789") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                enabled = !isLoading,
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Number,
                    imeAction = ImeAction.Next
                )
            )

            Spacer(modifier = Modifier.height(8.dp))

            // Credentials section
            Text(
                text = "Credentials",
                style = MaterialTheme.typography.titleMedium
            )

            OutlinedTextField(
                value = username,
                onValueChange = { username = it },
                label = { Text("Username") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                enabled = !isLoading,
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Text,
                    imeAction = ImeAction.Next
                )
            )

            OutlinedTextField(
                value = password,
                onValueChange = { password = it },
                label = { Text("Password") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                enabled = !isLoading,
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Password,
                    imeAction = ImeAction.Done
                )
            )

            // Status/Error messages
            statusMessage?.let {
                Text(
                    text = it,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.primary
                )
            }

            errorMessage?.let {
                Text(
                    text = it,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.error
                )
            }

            Spacer(modifier = Modifier.height(16.dp))

            // Login button
            Button(
                onClick = {
                    scope.launch {
                        isLoading = true
                        errorMessage = null
                        statusMessage = "Connecting..."

                        val host = serverHost.trim()
                        val port = serverPort.toIntOrNull() ?: 6789

                        if (host.isEmpty()) {
                            errorMessage = "Server address is required"
                            isLoading = false
                            statusMessage = null
                            return@launch
                        }

                        if (username.isEmpty() || password.isEmpty()) {
                            errorMessage = "Username and password are required"
                            isLoading = false
                            statusMessage = null
                            return@launch
                        }

                        val deviceId = credentialStorage.deviceId
                        val deviceName = Build.MODEL
                        val client = KryptonClient(deviceId, deviceName)

                        try {
                            // Connect
                            if (!client.connect(host, port)) {
                                errorMessage = "Failed to connect to server"
                                isLoading = false
                                statusMessage = null
                                client.shutdown()
                                return@launch
                            }

                            statusMessage = "Connected, authenticating..."

                            // Authenticate
                            client.authenticateWithCredentials(username, password)

                            // Wait for auth response
                            client.events.collectLatest { event ->
                                when (event) {
                                    is KryptonEvent.AuthResult -> {
                                        if (event.success) {
                                            val apiKey = event.apiKey
                                            if (apiKey != null) {
                                                // Save credentials
                                                credentialStorage.saveConnectionInfo(host, port, apiKey)
                                                statusMessage = "Login successful!"
                                                client.shutdown()
                                                onLoginSuccess()
                                            } else {
                                                errorMessage = "Login succeeded but no API key received"
                                                statusMessage = null
                                            }
                                        } else {
                                            errorMessage = event.message.ifEmpty { "Authentication failed" }
                                            statusMessage = null
                                        }
                                        isLoading = false
                                        client.shutdown()
                                        return@collectLatest
                                    }
                                    is KryptonEvent.ConnectionError -> {
                                        errorMessage = event.message
                                        statusMessage = null
                                        isLoading = false
                                        client.shutdown()
                                        return@collectLatest
                                    }
                                    else -> {}
                                }
                            }
                        } catch (e: Exception) {
                            errorMessage = e.message ?: "Connection error"
                            statusMessage = null
                            isLoading = false
                            client.shutdown()
                        }
                    }
                },
                modifier = Modifier.fillMaxWidth(),
                enabled = !isLoading
            ) {
                if (isLoading) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(24.dp),
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                } else {
                    Text("Connect & Login")
                }
            }

            // Info text
            Text(
                text = "Your username and password are only used once to obtain an API key. " +
                        "Only the API key and server address are stored on this device.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}
