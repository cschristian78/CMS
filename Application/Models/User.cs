using System.ComponentModel.DataAnnotations;

namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tUser</c> + <c>dbo.vUser</c>. In the CMS_PhaseI_All schema the inline
/// address columns moved out to <c>dbo.tUserAddress</c>; the user row gains <see cref="UserType"/>
/// (<c>Internal</c>/<c>External</c>) and soft-delete fields.</summary>
public sealed class User
{
    public int UserID { get; set; }

    [Required]
    [MaxLength(50)]
    public string? FirstName { get; set; }

    [Required]
    [MaxLength(50)]
    public string? LastName { get; set; }

    /// <summary>Computed at read-time as <c>CONCAT(FirstName, ' ', LastName)</c>.</summary>
    public string? FullName { get; set; }

    [MaxLength(100)]
    public string? Title { get; set; }

    [Required]
    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(150)]
    public string? Company { get; set; }

    [MaxLength(100)]
    public string? Department { get; set; }

    /// <summary>'Internal' or 'External' per <c>CK_tUser_UserType</c>.</summary>
    [Required]
    [MaxLength(20)]
    public string UserType { get; set; } = "Internal";

    public int StatusID { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusDescription { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Optional primary address materialised from <c>dbo.tUserAddress</c> for convenience
    /// (e.g., when populating directory rows or single-address forms).</summary>
    public UserAddress? PrimaryAddress { get; set; }

    /// <summary>All addresses from <c>dbo.tUserAddress</c> for this user, when loaded.</summary>
    public IReadOnlyList<UserAddress> Addresses { get; set; } = [];

    public string DisplayName
    {
        get
        {
            var name = !string.IsNullOrWhiteSpace(FullName) ? FullName : $"{FirstName} {LastName}".Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Email ?? $"User {UserID}";
            }

            return string.IsNullOrWhiteSpace(Email) ? name : $"{name} - {Email}";
        }
    }

    public string NameForClientAssignment
    {
        get
        {
            var value = !string.IsNullOrWhiteSpace(FullName) ? FullName! : $"{FirstName} {LastName}".Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                value = Email ?? string.Empty;
            }

            return value.Length <= 100 ? value : value[..100];
        }
    }
}
