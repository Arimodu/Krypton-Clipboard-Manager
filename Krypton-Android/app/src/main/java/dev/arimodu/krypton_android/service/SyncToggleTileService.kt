package dev.arimodu.krypton.service

import android.content.Intent
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import dev.arimodu.krypton.data.CredentialStorage

class SyncToggleTileService : TileService() {

    private val credentialStorage by lazy { CredentialStorage(this) }

    override fun onStartListening() {
        super.onStartListening()
        updateTileState()
    }

    override fun onClick() {
        super.onClick()

        // Toggle sync enabled state
        val newState = !credentialStorage.syncEnabled
        credentialStorage.syncEnabled = newState

        // Start or stop the service based on new state
        if (newState && credentialStorage.hasCredentials) {
            startSyncService()
        } else {
            stopSyncService()
        }

        updateTileState()
    }

    private fun updateTileState() {
        val tile = qsTile ?: return

        val syncEnabled = credentialStorage.syncEnabled
        val hasCredentials = credentialStorage.hasCredentials
        val serviceRunning = ClipboardSyncService.isRunning()

        tile.state = when {
            !hasCredentials -> Tile.STATE_UNAVAILABLE
            syncEnabled -> Tile.STATE_ACTIVE
            else -> Tile.STATE_INACTIVE
        }

        tile.label = "Krypton Sync"
        tile.subtitle = when {
            !hasCredentials -> "Not configured"
            syncEnabled && serviceRunning -> "Connected"
            syncEnabled -> "Connecting..."
            else -> "Disabled"
        }

        tile.updateTile()
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

    private fun stopSyncService() {
        val intent = Intent(this, ClipboardSyncService::class.java).apply {
            action = ClipboardSyncService.ACTION_STOP
        }
        startService(intent)
    }
}
