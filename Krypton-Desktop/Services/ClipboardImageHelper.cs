using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Serilog;

namespace Krypton_Desktop.Services;

/// <summary>
/// Platform-specific clipboard image helper. Reads and writes PNG bytes to/from the system clipboard.
/// All non-PNG formats (JPEG, WebP, BMP, TIFF, CF_DIB) are normalized to PNG via Avalonia/Skia.
/// </summary>
public static class ClipboardImageHelper
{
    // PNG file signature: 8 bytes
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Returns PNG bytes from the clipboard, or null if no image is present.
    /// </summary>
    public static async Task<byte[]?> TryReadImageAsPngAsync()
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416
            return await Task.Run(TryReadImageAsPngWindows);
#pragma warning restore CA1416
        }
        if (OperatingSystem.IsLinux())
            return await TryReadImageAsPngLinuxAsync();
        if (OperatingSystem.IsMacOS())
        {
#pragma warning disable CA1416
            return await Task.Run(TryReadImageAsPngMacOS);
#pragma warning restore CA1416
        }
        return null;
    }

    /// <summary>
    /// Writes PNG bytes to the clipboard so the image can be pasted.
    /// </summary>
    public static async Task<bool> WriteImageAsync(byte[] pngBytes)
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416
            return await Task.Run(() => WriteImageWindows(pngBytes));
#pragma warning restore CA1416
        }
        if (OperatingSystem.IsLinux())
            return await WriteImageLinuxAsync(pngBytes);
        if (OperatingSystem.IsMacOS())
        {
#pragma warning disable CA1416
            return await Task.Run(() => WriteImageMacOS(pngBytes));
#pragma warning restore CA1416
        }
        return false;
    }

    /// <summary>
    /// Parses the PNG IHDR chunk for width and height. Requires a valid PNG signature.
    /// </summary>
    internal static bool TryGetPngDimensions(byte[] pngBytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (pngBytes.Length < 24)
            return false;

        // Verify PNG signature
        for (int i = 0; i < PngSignature.Length; i++)
        {
            if (pngBytes[i] != PngSignature[i])
                return false;
        }

        // IHDR chunk: starts at byte 8
        // Width at bytes 16..19, Height at bytes 20..23 (big-endian)
        width = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
        height = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];
        return true;
    }

    /// <summary>
    /// Decodes an image from any format supported by Avalonia/Skia and returns PNG bytes.
    /// </summary>
    private static byte[]? NormalizeToPng(byte[] imageBytes)
    {
        try
        {
            using var inputStream = new MemoryStream(imageBytes);
            using var bitmap = new Bitmap(inputStream);
            using var outputStream = new MemoryStream();
            bitmap.Save(outputStream);
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to normalize image to PNG");
            return null;
        }
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static byte[]? TryReadImageAsPngWindows()
    {
        // Try registered "PNG" format first (Chrome, Firefox, modern apps)
        var pngFormatId = RegisterClipboardFormat("PNG");
        if (pngFormatId != 0 && IsClipboardFormatAvailable(pngFormatId))
        {
            var pngBytes = ReadClipboardFormatBytes(pngFormatId);
            if (pngBytes != null && pngBytes.Length > 0)
                return pngBytes;
        }

        // Fall back to CF_DIB (8) — Device-Independent Bitmap
        if (IsClipboardFormatAvailable(8))
        {
            var dibBytes = ReadClipboardFormatBytes(8);
            if (dibBytes != null && dibBytes.Length > 0)
                return ConvertDibToPng(dibBytes);
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static byte[]? ReadClipboardFormatBytes(uint format)
    {
        if (!OpenClipboard(IntPtr.Zero))
            return null;

        try
        {
            var handle = GetClipboardData(format);
            if (handle == IntPtr.Zero)
                return null;

            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                return null;

            try
            {
                var size = (int)GlobalSize(handle);
                if (size <= 0)
                    return null;

                var bytes = new byte[size];
                Marshal.Copy(ptr, bytes, 0, size);
                return bytes;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[]? ConvertDibToPng(byte[] dibBytes)
    {
        try
        {
            // DIB starts with BITMAPINFOHEADER (biSize at bytes 0..3)
            if (dibBytes.Length < 40)
                return null;

            var biSize = BitConverter.ToInt32(dibBytes, 0);
            var biClrUsed = BitConverter.ToInt32(dibBytes, 32);
            var biBitCount = BitConverter.ToInt16(dibBytes, 14);

            // Calculate color table size
            var colorTableEntries = biClrUsed != 0 ? biClrUsed : (biBitCount <= 8 ? (1 << biBitCount) : 0);
            var colorTableBytes = colorTableEntries * 4;

            // Construct BITMAPFILEHEADER (14 bytes)
            var bfOffBits = 14 + biSize + colorTableBytes;
            var bfSize = 14 + dibBytes.Length;

            var fileBytes = new byte[bfSize];

            // 'BM' signature
            fileBytes[0] = 0x42;
            fileBytes[1] = 0x4D;

            // bfSize (4 bytes, little-endian)
            BitConverter.TryWriteBytes(fileBytes.AsSpan(2), bfSize);

            // bfReserved1, bfReserved2 (4 bytes, zero)
            // already zero

            // bfOffBits (4 bytes)
            BitConverter.TryWriteBytes(fileBytes.AsSpan(10), bfOffBits);

            // Copy DIB data after header
            Buffer.BlockCopy(dibBytes, 0, fileBytes, 14, dibBytes.Length);

            return NormalizeToPng(fileBytes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to convert CF_DIB to PNG");
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool WriteImageWindows(byte[] pngBytes)
    {
        var pngFormatId = RegisterClipboardFormat("PNG");
        if (pngFormatId == 0)
            return false;

        if (!OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            EmptyClipboard();

            // Allocate global memory and copy PNG bytes
            var hMem = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)pngBytes.Length);
            if (hMem == IntPtr.Zero)
                return false;

            var ptr = GlobalLock(hMem);
            if (ptr == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return false;
            }

            try
            {
                Marshal.Copy(pngBytes, 0, ptr, pngBytes.Length);
            }
            finally
            {
                GlobalUnlock(hMem);
            }

            var result = SetClipboardData(pngFormatId, hMem);
            // SetClipboardData takes ownership of hMem on success; don't free it
            if (result == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    // Windows P/Invoke declarations
    [DllImport("user32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [SupportedOSPlatform("windows")]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    // ── Linux ─────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("linux")]
    private static async Task<byte[]?> TryReadImageAsPngLinuxAsync()
    {
        // Priority: image/png > image/jpeg > image/webp > image/bmp
        string[] preferredFormats = ["image/png", "image/jpeg", "image/webp", "image/bmp"];

        // Try xclip first, then wl-paste
        var availableFormats = await RunProcessAsync("xclip", "-selection clipboard -t TARGETS -o", null);
        if (availableFormats == null)
        {
            // Try Wayland
            availableFormats = await RunProcessAsync("wl-paste", "--list-types", null);
        }

        if (availableFormats == null)
            return null;

        var lines = availableFormats.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var format in preferredFormats)
        {
            if (!Array.Exists(lines, l => l.Trim().Equals(format, StringComparison.OrdinalIgnoreCase)))
                continue;

            byte[]? imageBytes = await ReadLinuxClipboardFormatAsync(format);
            if (imageBytes == null || imageBytes.Length == 0)
                continue;

            if (format == "image/png")
                return imageBytes;

            return NormalizeToPng(imageBytes);
        }

        return null;
    }

    [SupportedOSPlatform("linux")]
    private static async Task<byte[]?> ReadLinuxClipboardFormatAsync(string mimeType)
    {
        // Try xclip first
        var result = await RunProcessBytesAsync("xclip", $"-selection clipboard -t {mimeType} -o", null);
        if (result != null && result.Length > 0)
            return result;

        // Fall back to wl-paste (Wayland)
        result = await RunProcessBytesAsync("wl-paste", $"--type {mimeType}", null);
        return result;
    }

    [SupportedOSPlatform("linux")]
    private static async Task<bool> WriteImageLinuxAsync(byte[] pngBytes)
    {
        // Try xclip
        var result = await RunProcessBytesAsync("xclip", "-selection clipboard -t image/png -i", pngBytes);
        if (result != null)
            return true;

        // Fall back to wl-copy
        result = await RunProcessBytesAsync("wl-copy", "--type image/png", pngBytes);
        return result != null;
    }

    private static async Task<string?> RunProcessAsync(string executable, string arguments, byte[]? stdinData)
    {
        var bytes = await RunProcessBytesAsync(executable, arguments, stdinData);
        if (bytes == null) return null;
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static async Task<byte[]?> RunProcessBytesAsync(string executable, string arguments, byte[]? stdinData)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = stdinData != null,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();

            if (stdinData != null)
            {
                await process.StandardInput.BaseStream.WriteAsync(stdinData);
                process.StandardInput.Close();
            }

            using var ms = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms);
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                return null;

            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    // ── macOS ─────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("osx")]
    private static byte[]? TryReadImageAsPngMacOS()
    {
        try
        {
            var pasteboard = ObjC_GetGeneralPasteboard();
            if (pasteboard == IntPtr.Zero) return null;

            // Try PNG first, then TIFF (macOS default for screenshots)
            byte[]? data = ObjC_GetPasteboardData(pasteboard, "public.png")
                        ?? ObjC_GetPasteboardData(pasteboard, "public.tiff");

            if (data == null || data.Length == 0)
                return null;

            // Check if already PNG
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50)
                return data;

            // Normalize (e.g. TIFF → PNG)
            return NormalizeToPng(data);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read image from macOS clipboard");
            return null;
        }
    }

    [SupportedOSPlatform("osx")]
    private static bool WriteImageMacOS(byte[] pngBytes)
    {
        try
        {
            var pasteboard = ObjC_GetGeneralPasteboard();
            if (pasteboard == IntPtr.Zero) return false;

            // clearContents
            var clearSel = sel_registerName("clearContents");
            objc_msgSend(pasteboard, clearSel);

            // setData:forType:
            var nsDataClass = objc_getClass("NSData");
            var dataWithBytesSel = sel_registerName("dataWithBytes:length:");
            IntPtr nsData;
            unsafe
            {
                fixed (byte* ptr = pngBytes)
                {
                    nsData = objc_msgSend(nsDataClass, dataWithBytesSel, (IntPtr)ptr, (IntPtr)pngBytes.Length);
                }
            }

            var typeStr = ObjC_CreateNSString("public.png");
            var setDataSel = sel_registerName("setData:forType:");
            objc_msgSend(pasteboard, setDataSel, nsData, typeStr);

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write image to macOS clipboard");
            return false;
        }
    }

    [SupportedOSPlatform("osx")]
    private static IntPtr ObjC_GetGeneralPasteboard()
    {
        var nsClass = objc_getClass("NSPasteboard");
        var sel = sel_registerName("generalPasteboard");
        return objc_msgSend(nsClass, sel);
    }

    [SupportedOSPlatform("osx")]
    private static byte[]? ObjC_GetPasteboardData(IntPtr pasteboard, string uti)
    {
        var typeStr = ObjC_CreateNSString(uti);
        var availableTypeSel = sel_registerName("availableTypeFromArray:");
        var nsArrayClass = objc_getClass("NSArray");
        var arrayWithObjectSel = sel_registerName("arrayWithObject:");
        var typeArray = objc_msgSend(nsArrayClass, arrayWithObjectSel, typeStr);

        var available = objc_msgSend(pasteboard, availableTypeSel, typeArray);
        if (available == IntPtr.Zero)
            return null;

        var dataForTypeSel = sel_registerName("dataForType:");
        var nsData = objc_msgSend(pasteboard, dataForTypeSel, typeStr);
        if (nsData == IntPtr.Zero)
            return null;

        var lengthSel = sel_registerName("length");
        var bytesSel = sel_registerName("bytes");
        var length = (int)(IntPtr)objc_msgSend(nsData, lengthSel);
        if (length <= 0)
            return null;

        var bytesPtr = objc_msgSend(nsData, bytesSel);
        if (bytesPtr == IntPtr.Zero)
            return null;

        var result = new byte[length];
        Marshal.Copy(bytesPtr, result, 0, length);
        return result;
    }

    [SupportedOSPlatform("osx")]
    private static IntPtr ObjC_CreateNSString(string str)
    {
        var nsStringClass = objc_getClass("NSString");
        var initSel = sel_registerName("stringWithUTF8String:");
        var bytes = System.Text.Encoding.UTF8.GetBytes(str + "\0");
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                return objc_msgSend(nsStringClass, initSel, (IntPtr)ptr);
            }
        }
    }

    // macOS ObjC runtime P/Invoke
    [DllImport("/usr/lib/libobjc.dylib")]
    [SupportedOSPlatform("osx")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    [SupportedOSPlatform("osx")]
    private static extern IntPtr sel_registerName(string str);

    [DllImport("/usr/lib/libobjc.dylib")]
    [SupportedOSPlatform("osx")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    [SupportedOSPlatform("osx")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport("/usr/lib/libobjc.dylib")]
    [SupportedOSPlatform("osx")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);
}
