using Krypton.Shared.Protocol;
using System;

namespace Krypton_Desktop.Models
{

    /// <summary>
    /// Represents a clipboard entry in the local history.
    /// </summary>
    public class ClipboardItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public ClipboardContentType ContentType { get; set; } = ClipboardContentType.Text;
        public byte[] Content { get; set; } = [];
        public string Preview { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? SourceDevice { get; set; }
        public bool IsSynced { get; set; }
        public string? ServerId { get; set; }

        /// <summary>
        /// Gets the text content if this is a text item.
        /// </summary>
        public string? TextContent =>
            ContentType == ClipboardContentType.Text
                ? System.Text.Encoding.UTF8.GetString(Content)
                : null;

        /// <summary>
        /// Creates a ClipboardItem from text content.
        /// </summary>
        public static ClipboardItem FromText(string text)
        {
            var content = System.Text.Encoding.UTF8.GetBytes(text);
            return new ClipboardItem
            {
                ContentType = ClipboardContentType.Text,
                Content = content,
                Preview = text.Length > 200 ? text[..200] + "..." : text,
                ContentHash = ComputeHash(content)
            };
        }

        /// <summary>
        /// Creates a ClipboardItem from PNG image bytes.
        /// </summary>
        public static ClipboardItem FromImage(byte[] pngBytes, string? sourceDevice = null)
        {
            Services.ClipboardImageHelper.TryGetPngDimensions(pngBytes, out var w, out var h);
            var sizeKb = pngBytes.Length / 1024;
            return new ClipboardItem
            {
                ContentType = ClipboardContentType.Image,
                Content = pngBytes,
                Preview = $"[Image: {w}×{h} px, {sizeKb} KB]",
                ContentHash = ComputeHash(pngBytes),
                SourceDevice = sourceDevice
            };
        }

        /// <summary>
        /// Creates a ClipboardItem from a proto entry.
        /// </summary>
        public static ClipboardItem FromProto(ClipboardEntry entry)
        {
            return new ClipboardItem
            {
                ServerId = entry.Id,
                ContentType = entry.ContentType,
                Content = entry.Content.ToByteArray(),
                Preview = entry.ContentPreview,
                ContentHash = entry.ContentHash,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)entry.CreatedAt).UtcDateTime,
                SourceDevice = entry.SourceDevice,
                IsSynced = true
            };
        }

        private static string ComputeHash(byte[] content)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(content);
            return Convert.ToHexString(hash);
        }
    }
}