package dev.arimodu.krypton.service

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import android.util.Log
import dev.arimodu.krypton.data.CredentialStorage

class BootReceiver : BroadcastReceiver() {
    companion object {
        private const val TAG = "BootReceiver"
    }

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED ||
            intent.action == "android.intent.action.QUICKBOOT_POWERON" ||
            intent.action == "com.htc.intent.action.QUICKBOOT_POWERON") {

            Log.i(TAG, "Boot completed, checking if service should start")

            val credentialStorage = CredentialStorage(context)

            if (credentialStorage.hasCredentials && credentialStorage.syncEnabled) {
                Log.i(TAG, "Starting ClipboardSyncService on boot")

                val serviceIntent = Intent(context, ClipboardSyncService::class.java).apply {
                    action = ClipboardSyncService.ACTION_START
                }

                try {
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                        context.startForegroundService(serviceIntent)
                    } else {
                        context.startService(serviceIntent)
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Failed to start service on boot", e)
                }
            } else {
                Log.i(TAG, "Skipping service start: hasCredentials=${credentialStorage.hasCredentials}, syncEnabled=${credentialStorage.syncEnabled}")
            }
        }
    }
}
