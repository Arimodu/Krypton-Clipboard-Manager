using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Krypton.Shared.Protocol;

namespace Krypton.Server.Database.Entities;

public class ClipboardEntry
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    public ClipboardContentType ContentType { get; set; }

    [Required]
    public byte[] Content { get; set; } = Array.Empty<byte>();

    [MaxLength(200)]
    public string? ContentPreview { get; set; }

    [Required]
    [MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? SourceDevice { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime SyncedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
