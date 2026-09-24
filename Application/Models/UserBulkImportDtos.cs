using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ClientManagementSystem.Models;

public sealed class UserImportRowDto
{
    public int LineNumber { get; set; }

    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(150)]
    public string Company { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Department { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    /// <summary>'Basic', 'External', 'Administrator', 'Manager', or 'Analyst' per <c>CK_tUser_UserType</c>.</summary>
    [MaxLength(20)]
    public string UserType { get; set; } = "Basic";

    /// <summary>Maps to <c>dbo.tUser.IsActive</c>. New users default to active (1) when the
    /// column is omitted from the CSV.</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>Optional primary address fields. Empty <see cref="Address1"/> means no address
    /// is created for this row.</summary>
    [MaxLength(20)]
    public string AddressType { get; set; } = "Work";
    public string Address1 { get; set; } = string.Empty;
    public string Address2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public sealed class UserBulkParseResult
{
    public IReadOnlyList<UserImportRowDto> Rows { get; set; } = [];
    public int SkippedEmptyRows { get; set; }
}

public sealed class UserBulkVerifyResult
{
    public bool Valid { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
}

public sealed class UserBulkRowsRequest
{
    public IReadOnlyList<UserImportRowDto> Rows { get; set; } = [];
}

public sealed class UserBulkImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedEmptyRows { get; set; }
    public IReadOnlyList<string> RowErrors { get; set; } = [];
}
