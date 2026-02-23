package dev.arimodu.krypton.service

import android.app.Activity
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.graphics.PixelFormat
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.view.Gravity
import android.view.LayoutInflater
import android.view.View
import android.view.ViewTreeObserver
import android.view.WindowManager
import dev.arimodu.krypton.R

/**
 * A transparent overlay activity that gains focus to read clipboard content.
 * On Android 10+, clipboard can only be read when the app has focus.
 * This activity creates a minimal overlay, gains focus, reads the clipboard,
 * and immediately finishes.
 *
 * Based on ClipCascade's approach for reliable clipboard access.
 */
class ClipboardFloatingActivity : Activity() {
    companion object {
        private const val TAG = "ClipboardFloating"
        const val EXTRA_SOURCE = "source"
        const val SOURCE_LOGCAT = "logcat"
        const val SOURCE_MANUAL = "manual"

        // Debounce to prevent rapid re-triggering
        private const val DEBOUNCE_MS = 1000L

        @Volatile
        private var isActive = false

        @Volatile
        private var lastLaunchTime = 0L

        fun isRunning(): Boolean = isActive

        fun launch(context: Context, source: String = SOURCE_MANUAL) {
            val now = System.currentTimeMillis()
            if (isActive || (now - lastLaunchTime) < DEBOUNCE_MS) {
                Log.d(TAG, "Skipping launch - active: $isActive, debounce: ${now - lastLaunchTime}ms")
                return
            }
            lastLaunchTime = now

            val intent = Intent(context, ClipboardFloatingActivity::class.java).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                addFlags(Intent.FLAG_ACTIVITY_NO_ANIMATION)
                addFlags(Intent.FLAG_ACTIVITY_EXCLUDE_FROM_RECENTS)
                addFlags(Intent.FLAG_ACTIVITY_NO_HISTORY)
                putExtra(EXTRA_SOURCE, source)
            }
            context.startActivity(intent)
        }
    }

    private var windowManager: WindowManager? = null
    private var floatingView: View? = null
    private var layoutParams: WindowManager.LayoutParams? = null
    private val handler = Handler(Looper.getMainLooper())
    private var clipboardRead = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        isActive = true
        Log.d(TAG, "ClipboardFloatingActivity created")

        // Make this activity completely transparent
        window.setBackgroundDrawableResource(android.R.color.transparent)

        // Create the floating overlay view
        createFloatingView()
    }

    private fun createFloatingView() {
        try {
            windowManager = getSystemService(Context.WINDOW_SERVICE) as WindowManager

            layoutParams = WindowManager.LayoutParams().apply {
                width = WindowManager.LayoutParams.WRAP_CONTENT
                height = WindowManager.LayoutParams.WRAP_CONTENT
                gravity = Gravity.TOP or Gravity.START
                x = 0
                y = 0

                type = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                } else {
                    @Suppress("DEPRECATION")
                    WindowManager.LayoutParams.TYPE_PHONE
                }

                // Start NOT focusable, will request focus after view is ready
                flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                        WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH

                format = PixelFormat.TRANSLUCENT
            }

            floatingView = LayoutInflater.from(this).inflate(R.layout.floating_view_layout, null)

            // Add listener to detect when view is laid out
            floatingView?.viewTreeObserver?.addOnGlobalLayoutListener(object : ViewTreeObserver.OnGlobalLayoutListener {
                override fun onGlobalLayout() {
                    floatingView?.viewTreeObserver?.removeOnGlobalLayoutListener(this)

                    // View is now laid out, request focus
                    handler.post {
                        makeFloatingViewFocusable()
                    }
                }
            })

            windowManager?.addView(floatingView, layoutParams)
            Log.d(TAG, "Floating view created")
        } catch (e: Exception) {
            Log.e(TAG, "Failed to create floating view", e)
            finishAndCleanup()
        }
    }

    private fun makeFloatingViewFocusable() {
        try {
            layoutParams?.let { params ->
                // Remove FLAG_NOT_FOCUSABLE to gain focus
                params.flags = params.flags and WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE.inv()
                windowManager?.updateViewLayout(floatingView, params)
                Log.d(TAG, "Made view focusable")

                // Small delay then read clipboard
                handler.postDelayed({
                    readClipboard()
                }, 50)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Failed to make view focusable", e)
            finishAndCleanup()
        }
    }

    private fun makeFloatingViewUnfocusable() {
        try {
            layoutParams?.let { params ->
                // Add FLAG_NOT_FOCUSABLE to lose focus
                params.flags = params.flags or WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                windowManager?.updateViewLayout(floatingView, params)
                Log.d(TAG, "Made view unfocusable")
            }
        } catch (e: Exception) {
            Log.w(TAG, "Failed to make view unfocusable", e)
        }
    }

    private fun readClipboard() {
        if (clipboardRead) return
        clipboardRead = true

        try {
            val clipboardManager = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            val clip = clipboardManager.primaryClip

            if (clip != null && clip.itemCount > 0) {
                val description = clip.description
                if (description.hasMimeType(ClipDescription.MIMETYPE_TEXT_PLAIN) ||
                    description.hasMimeType(ClipDescription.MIMETYPE_TEXT_HTML)) {

                    val item = clip.getItemAt(0)
                    val text = item?.coerceToText(this)?.toString()

                    if (!text.isNullOrEmpty()) {
                        Log.d(TAG, "Clipboard read successfully: ${text.take(50)}...")

                        // Send to the service
                        ClipboardSyncService.getInstance()?.onClipboardReadFromOverlay(text)
                    } else {
                        Log.d(TAG, "Clipboard text is empty")
                    }
                } else {
                    Log.d(TAG, "Clipboard is not text: ${description.getMimeType(0)}")
                }
            } else {
                Log.d(TAG, "Clipboard is empty or null")
            }
        } catch (e: Exception) {
            Log.e(TAG, "Failed to read clipboard", e)
        } finally {
            // Lose focus then finish
            makeFloatingViewUnfocusable()
            handler.postDelayed({
                finishAndCleanup()
            }, 50)
        }
    }

    private fun finishAndCleanup() {
        cleanup()
        finish()
    }

    private fun cleanup() {
        try {
            floatingView?.let { view ->
                windowManager?.removeView(view)
            }
        } catch (e: Exception) {
            Log.w(TAG, "Error removing floating view", e)
        }
        floatingView = null
        windowManager = null
        layoutParams = null
    }

    override fun onDestroy() {
        cleanup()
        isActive = false
        Log.d(TAG, "ClipboardFloatingActivity destroyed")
        super.onDestroy()
    }

    override fun finish() {
        super.finish()
        // Disable exit animation
        @Suppress("DEPRECATION")
        overridePendingTransition(0, 0)
    }
}
