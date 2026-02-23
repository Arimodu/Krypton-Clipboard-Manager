using System.ComponentModel.DataAnnotations;

namespace Krypton.Server.Database.Entities;

public class User
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();

    public ICollection<ClipboardEntry> ClipboardEntries { get; set; } = new List<ClipboardEntry>();
}
