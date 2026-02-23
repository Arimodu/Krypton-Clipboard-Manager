package dev.arimodu.krypton.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import java.util.UUID

class CredentialStorage(context: Context) {
    companion object {
        private const val PREFS_NAME = "krypton_secure_prefs"
        private const val KEY_SERVER_HOST = "server_host"
        private const val KEY_SERVER_PORT = "server_port"
        private const val KEY_API_KEY = "api_key"
        private const val KEY_DEVICE_ID = "device_id"
        private const val KEY_DEVICE_NAME = "device_name"
        private const val KEY_SYNC_ENABLED = "sync_enabled"
        private const val KEY_SETUP_COMPLETED = "setup_completed"
        private const val KEY_SHOW_NOTIFICATION = "show_notification"
        private const val KEY_ADB_PERMISSION_ACKNOWLEDGED = "adb_permission_acknowledged"
        private const val DEFAULT_PORT = 6789
    }

    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val prefs: SharedPreferences = EncryptedSharedPreferences.create(
        context,
        PREFS_NAME,
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    var serverHost: String?
        get() = prefs.getString(KEY_SERVER_HOST, null)
        set(value) = prefs.edit().putString(KEY_SERVER_HOST, value).apply()

    var serverPort: Int
        get() = prefs.getInt(KEY_SERVER_PORT, DEFAULT_PORT)
        set(value) = prefs.edit().putInt(KEY_SERVER_PORT, value).apply()

    var apiKey: String?
        get() = prefs.getString(KEY_API_KEY, null)
        set(value) = prefs.edit().putString(KEY_API_KEY, value).apply()

    var syncEnabled: Boolean
        get() = prefs.getBoolean(KEY_SYNC_ENABLED, true)
        set(value) = prefs.edit().putBoolean(KEY_SYNC_ENABLED, value).apply()

    var setupCompleted: Boolean
        get() = prefs.getBoolean(KEY_SETUP_COMPLETED, false)
        set(value) = prefs.edit().putBoolean(KEY_SETUP_COMPLETED, value).apply()

    var showNotification: Boolean
        get() = prefs.getBoolean(KEY_SHOW_NOTIFICATION, true)
        set(value) = prefs.edit().putBoolean(KEY_SHOW_NOTIFICATION, value).apply()

    var adbPermissionAcknowledged: Boolean
        get() = prefs.getBoolean(KEY_ADB_PERMISSION_ACKNOWLEDGED, false)
        set(value) = prefs.edit().putBoolean(KEY_ADB_PERMISSION_ACKNOWLEDGED, value).apply()

    var deviceName: String
        get() = prefs.getString(KEY_DEVICE_NAME, null) ?: android.os.Build.MODEL
        set(value) = prefs.edit().putString(KEY_DEVICE_NAME, value).apply()

    val deviceId: String
        get() {
            var id = prefs.getString(KEY_DEVICE_ID, null)
            if (id == null) {
                id = UUID.randomUUID().toString()
                prefs.edit().putString(KEY_DEVICE_ID, id).apply()
            }
            return id
        }

    val hasCredentials: Boolean
        get() = !serverHost.isNullOrEmpty() && !apiKey.isNullOrEmpty()

    fun saveConnectionInfo(host: String, port: Int, apiKey: String) {
        prefs.edit()
            .putString(KEY_SERVER_HOST, host)
            .putInt(KEY_SERVER_PORT, port)
            .putString(KEY_API_KEY, apiKey)
            .apply()
    }

    fun clearCredentials() {
        prefs.edit()
            .remove(KEY_API_KEY)
            .apply()
    }

    fun clearAll() {
        prefs.edit()
            .remove(KEY_SERVER_HOST)
            .remove(KEY_SERVER_PORT)
            .remove(KEY_API_KEY)
            .apply()
    }
}
