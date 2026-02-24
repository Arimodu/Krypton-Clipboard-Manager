using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace Krypton_Desktop.Services.Platform;

/// <summary>
/// macOS-specific permission helpers and process policy management.
/// Uses the Objective-C runtime and ApplicationServices framework via P/Invoke.
/// </summary>
[SupportedOSPlatform("macos")]
public static class MacOSPermissions
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string AppServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string IOKit =
        "/System/Library/Frameworks/IOKit.framework/IOKit";

    // ── ApplicationServices — Accessibility ───────────────────────────────────

    /// <summary>Returns true if the process already has Accessibility access.</summary>
    // macOS returns a 1-byte Boolean; marshal as U1 so we don't read 3 garbage bytes.
    [DllImport(AppServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXIsProcessTrusted();

    /// <summary>
    /// Checks Accessibility access and, when the options dict contains
    /// kAXTrustedCheckOptionPrompt = YES, shows the system alert directing
    /// the user to grant access in System Settings.
    /// </summary>
    [DllImport(AppServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    // ── IOKit — Input Monitoring (authoritative TCC check) ────────────────────

    /// <summary>
    /// Checks Input Monitoring TCC access without triggering a prompt.
    /// kIOHIDRequestTypeListenEvent = 1.
    /// Returns: kIOHIDAccessTypeGranted = 0, kIOHIDAccessTypeDenied = 1, kIOHIDAccessTypeUnknown = 2.
    /// More reliable than CGPreflightListenEventAccess which can cache a stale result
    /// within a process lifetime.
    /// </summary>
    [DllImport(IOKit)]
    private static extern uint IOHIDCheckAccess(uint requestType);

    // ── CoreGraphics — Input Monitoring (fallback / prompt trigger) ───────────

    /// <summary>
    /// Returns true if the process already has Input Monitoring access.
    /// Can cache a stale false within a process lifetime; prefer IOHIDCheckAccess.
    /// </summary>
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CGPreflightListenEventAccess();

    /// <summary>
    /// Triggers the system prompt for Input Monitoring access and returns
    /// whether access is already granted.
    /// </summary>
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CGRequestListenEventAccess();

    // ── Objective-C runtime ───────────────────────────────────────────────────

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    // Returns an Objective-C object (id)
    [DllImport(LibObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    // One const char* argument (e.g. stringWithUTF8String:)
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_cstr(
        IntPtr receiver, IntPtr selector,
        [MarshalAs(UnmanagedType.LPStr)] string arg);

    // One BOOL (byte) argument, returns id (e.g. numberWithBool:)
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_byte(
        IntPtr receiver, IntPtr selector, byte arg);

    // Two id arguments, returns id (e.g. dictionaryWithObject:forKey:)
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_id_id(
        IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    // One NSInteger argument, returns BOOL (e.g. setActivationPolicy:)
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern bool objc_msgSend_nint(
        IntPtr receiver, IntPtr selector, nint arg);

    // One BOOL (byte) argument, returns void (e.g. activateIgnoringOtherApps:)
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_byte(
        IntPtr receiver, IntPtr selector, byte arg);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if Input Monitoring access has already been granted.
    /// Uses IOHIDCheckAccess (IOKit) as the primary check because
    /// CGPreflightListenEventAccess can cache a stale false result for the
    /// entire lifetime of a process.  Falls back to CGPreflight if IOKit
    /// throws (e.g. framework not found on a very old OS).
    /// </summary>
    public static bool HasInputMonitoringPermission()
    {
        try
        {
            // kIOHIDRequestTypeListenEvent = 1; kIOHIDAccessTypeGranted = 0
            return IOHIDCheckAccess(1) == 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "IOHIDCheckAccess unavailable, falling back to CGPreflightListenEventAccess");
            return CGPreflightListenEventAccess();
        }
    }

    /// <summary>
    /// Triggers the system prompt for Input Monitoring access.
    /// Returns true if already granted.
    /// </summary>
    public static bool RequestInputMonitoringPermission()
    {
        if (CGPreflightListenEventAccess()) return true;
        CGRequestListenEventAccess();
        Log.Information("macOS: Input Monitoring permission prompt shown");
        return false;
    }

    /// <summary>Returns true if Accessibility access has already been granted.</summary>
    public static bool HasAccessibilityPermission() => AXIsProcessTrusted();

    /// <summary>Returns true when all permissions Krypton requires are granted.</summary>
    public static bool AllPermissionsGranted() =>
        HasInputMonitoringPermission() && AXIsProcessTrusted();

    /// <summary>
    /// Triggers the macOS system alert that asks the user to grant Accessibility
    /// access (required for global hotkeys via SharpHook).  Returns true if
    /// access is already granted.  If the prompt cannot be raised the method
    /// falls back to opening System Settings directly.
    /// </summary>
    public static bool RequestAccessibilityPermission()
    {
        if (AXIsProcessTrusted()) return true;

        try
        {
            // Build NSDictionary: @{ @"AXTrustedCheckOptionPrompt": @YES }
            var nsStringClass  = objc_getClass("NSString");
            var stringWithUtf8 = sel_registerName("stringWithUTF8String:");
            var key = objc_msgSend_cstr(nsStringClass, stringWithUtf8,
                "AXTrustedCheckOptionPrompt");

            var nsNumberClass  = objc_getClass("NSNumber");
            var numberWithBool = sel_registerName("numberWithBool:");
            var value = objc_msgSend_byte(nsNumberClass, numberWithBool, 1); // YES

            var nsDictClass         = objc_getClass("NSDictionary");
            var dictionaryObjectKey = sel_registerName("dictionaryWithObject:forKey:");
            var options = objc_msgSend_id_id(nsDictClass, dictionaryObjectKey, value, key);

            AXIsProcessTrustedWithOptions(options);
            Log.Information("macOS: Accessibility permission prompt shown");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AXIsProcessTrustedWithOptions failed; opening System Settings");
            OpenAccessibilitySettings();
        }

        return false;
    }

    /// <summary>Opens System Settings → Privacy & Security → Accessibility.</summary>
    public static void OpenAccessibilitySettings() =>
        OpenSystemPreferencesPane("Privacy_Accessibility");

    /// <summary>Opens System Settings → Privacy & Security → Input Monitoring.</summary>
    public static void OpenInputMonitoringSettings() =>
        OpenSystemPreferencesPane("Privacy_ListenEvent");

    private static void OpenSystemPreferencesPane(string anchor)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"x-apple.systempreferences:com.apple.preference.security?{anchor}",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open System Settings pane: {Anchor}", anchor);
        }
    }

    /// <summary>
    /// Re-launches the app from the same executable path and exits the current process.
    /// Call after the user has granted permissions so the new instance starts clean.
    /// </summary>
    public static void RestartApp()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = false });
        }
        Environment.Exit(0);
    }

    /// <summary>
    /// Brings the app to the foreground so windows appear on top of other apps.
    /// Required on macOS when the app is in accessory/LSUIElement mode, because
    /// in that mode the app is a background process and windows won't surface
    /// automatically when shown from a hotkey callback.
    /// </summary>
    public static void ActivateApp()
    {
        try
        {
            var nsAppClass  = objc_getClass("NSApplication");
            var sharedApp   = sel_registerName("sharedApplication");
            var nsApp       = objc_msgSend(nsAppClass, sharedApp);
            var activateSel = sel_registerName("activateIgnoringOtherApps:");
            objc_msgSend_void_byte(nsApp, activateSel, 1); // YES
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to activate app");
        }
    }

    /// <summary>
    /// Hides the Dock icon at runtime by setting NSApplication's activation
    /// policy to Accessory (1).  Call this before any windows are shown.
    /// For .app bundle deployments the Info.plist LSUIElement key handles this
    /// declaratively; this call is a belt-and-suspenders fallback for running
    /// the raw executable outside the bundle during development.
    /// </summary>
    public static void HideFromDock()
    {
        try
        {
            var nsAppClass       = objc_getClass("NSApplication");
            var sharedApp        = sel_registerName("sharedApplication");
            var nsApp            = objc_msgSend(nsAppClass, sharedApp);
            var setPolicy        = sel_registerName("setActivationPolicy:");

            // NSApplicationActivationPolicyAccessory = 1
            objc_msgSend_nint(nsApp, setPolicy, 1);
            Log.Debug("macOS: activation policy set to Accessory (Dock icon hidden)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to hide app from Dock");
        }
    }
}
