using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/admin")]
[ApiController]
public sealed class AdminApiController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly IUserPhotoStorage _userPhotos;
    private readonly ILogger<AdminApiController> _logger;
    private readonly IHostEnvironment _environment;

    public AdminApiController(
        IAdminService adminService,
        IUserPhotoStorage userPhotos,
        ILogger<AdminApiController> logger,
        IHostEnvironment environment)
    {
        _adminService = adminService;
        _userPhotos = userPhotos;
        _logger = logger;
        _environment = environment;
    }

    [HttpGet]
    public async Task<ActionResult<AdminIndexViewModel>> GetAsync(CancellationToken cancellationToken)
        => Ok(await _adminService.GetIndexAsync(cancellationToken));

    [HttpPost("server/save")]
    public Task<ActionResult> SaveServerAsync([FromBody] ServerConnectionSettings settings, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            await _adminService.SaveServerSettingsAsync(settings, cancellationToken);
            return Ok(new { message = "Server settings were tested, encrypted, and saved." });
        });

    [HttpPost("server/test")]
    public async Task<ActionResult<ConnectionTestResult>> TestServerAsync(
        [FromBody] ServerConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.TestServerSettingsAsync(settings, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin API TestServer failed.");
            return Ok(ConnectionTestResult.Failure(UserFacingError(ex)));
        }
    }

    // ---- USERS ---------------------------------------------------------

    [HttpPost("users/save")]
    public Task<ActionResult> SaveUserAsync([FromBody] User user, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            var savedId = await _adminService.SaveUserAsync(user, cancellationToken);
            var effectiveId = user.UserID > 0 ? user.UserID : savedId;
            return Ok(new { message = "User saved.", userId = effectiveId });
        });

    [HttpPost("users/{userId:int}/soft-delete")]
    public Task<ActionResult> SoftDeleteUserAsync(int userId, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            await _adminService.SoftDeleteUserAsync(userId, cancellationToken);
            return Ok(new { message = "User soft-deleted." });
        });

    [HttpPost("users/{userId:int}/restore")]
    public Task<ActionResult> RestoreUserAsync(int userId, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            await _adminService.RestoreUserAsync(userId, cancellationToken);
            return Ok(new { message = "User restored." });
        });

    [HttpGet("users/{userId:int}/addresses")]
    public async Task<ActionResult<IReadOnlyList<UserAddress>>> ListUserAddressesAsync(int userId, CancellationToken cancellationToken)
        => Ok(await _adminService.ListUserAddressesAsync(userId, cancellationToken));

    [HttpPost("users/addresses/save")]
    public Task<ActionResult> SaveUserAddressAsync([FromBody] UserAddress address, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            var id = await _adminService.SaveUserAddressAsync(address, cancellationToken);
            return Ok(new { addressId = id });
        });

    [HttpDelete("users/addresses/{addressId:int}")]
    public Task<ActionResult> DeleteUserAddressAsync(int addressId, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            await _adminService.DeleteUserAddressAsync(addressId, cancellationToken);
            return Ok(new { message = "Address removed." });
        });

    [HttpGet("users/{userId:int}/photo")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult GetUserPhoto(int userId)
    {
        if (!_userPhotos.TryGetPhotoFile(userId, out var path, out var contentType))
        {
            return NotFound();
        }

        return PhysicalFile(path, contentType);
    }

    [HttpPost("users/{userId:int}/photo")]
    [RequestFormLimits(MultipartBodyLengthLimit = UserPhotoFileStorage.MaxPhotoBytes)]
    [RequestSizeLimit(UserPhotoFileStorage.MaxPhotoBytes)]
    public async Task<ActionResult> UploadUserPhotoAsync(int userId, IFormFile file, CancellationToken cancellationToken)
    {
        if (userId <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid user", Detail = "userId must be positive." });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No file", Detail = "Choose an image file." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            await _adminService.SaveUserPhotoAsync(userId, stream, file.Length, cancellationToken);
            return Ok(new { message = "Photo saved." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Photo rejected", Detail = ex.Message });
        }
    }

    [HttpDelete("users/{userId:int}/photo")]
    public IActionResult DeleteUserPhoto(int userId)
    {
        _userPhotos.DeletePhotos(userId);
        return Ok(new { message = "Photo removed." });
    }

    [HttpPost("users/bulk-import/parse")]
    [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<UserBulkParseResult>> ParseUserBulkImportAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No file", Detail = "Choose a CSV file to import." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await _adminService.ParseUserImportCsvAsync(stream, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Parse rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin user bulk import parse failed.");
            return Problem(detail: "Parse failed.", title: "Parse failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("users/bulk-import/verify")]
    public async Task<ActionResult<UserBulkVerifyResult>> VerifyUserBulkImportAsync(
        [FromBody] UserBulkRowsRequest body, CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        return Ok(await _adminService.VerifyUserImportRowsAsync(body.Rows, cancellationToken));
    }

    [HttpPost("users/bulk-import/commit")]
    public async Task<ActionResult<UserBulkImportResult>> CommitUserBulkImportAsync(
        [FromBody] UserBulkRowsRequest body, CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        try
        {
            return Ok(await _adminService.CommitUserImportRowsAsync(body.Rows, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Import not allowed", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin user bulk import commit failed.");
            return Problem(detail: "Import failed.", title: "Import failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ---- CLIENTS -------------------------------------------------------

    [HttpGet("clients")]
    public async Task<ActionResult<IReadOnlyList<ClientAdminRow>>> ListClientsAsync(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = true,
        CancellationToken cancellationToken = default)
        => Ok(await _adminService.ListClientsAsync(search, includeInactive, cancellationToken));

    [HttpPost("clients/save")]
    public Task<ActionResult> SaveClientAsync([FromBody] ClientAdminSaveRequest request, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            var clientId = await _adminService.SaveClientAsync(request, cancellationToken);
            var effectiveId = request.ClientID > 0 ? request.ClientID : clientId;
            return Ok(new { clientId = effectiveId, message = "Client saved." });
        });

    [HttpPost("clients/{clientId:int}/soft-delete")]
    public Task<ActionResult> SoftDeleteClientAsync(int clientId, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            await _adminService.SoftDeleteClientAsync(clientId, cancellationToken);
            return Ok(new { message = "Client deactivated." });
        });

    [HttpPost("clients/{clientId:int}/delete")]
    public Task<ActionResult> HardDeleteClientAsync(int clientId, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            await _adminService.HardDeleteClientAsync(clientId, cancellationToken);
            return Ok(new { message = "Client deleted." });
        });

    [HttpPost("clients/bulk-import/parse")]
    [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<ClientAdminBulkParseResult>> ParseClientBulkImportAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No file", Detail = "Choose a CSV file to import." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await _adminService.ParseClientImportCsvAsync(stream, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Parse rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin client bulk import parse failed.");
            return Problem(detail: "Parse failed.", title: "Parse failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("clients/bulk-import/verify")]
    public async Task<ActionResult<ClientAdminBulkVerifyResult>> VerifyClientBulkImportAsync(
        [FromBody] ClientAdminBulkRowsRequest body, CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        return Ok(await _adminService.VerifyClientImportRowsAsync(body.Rows, cancellationToken));
    }

    [HttpPost("clients/bulk-import/commit")]
    public async Task<ActionResult<ClientAdminBulkImportResult>> CommitClientBulkImportAsync(
        [FromBody] ClientAdminBulkRowsRequest body, CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        try
        {
            return Ok(await _adminService.CommitClientImportRowsAsync(body.Rows, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Import not allowed", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin client bulk import commit failed.");
            return Problem(detail: "Import failed.", title: "Import failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ---- LOOKUPS -------------------------------------------------------

    [HttpPost("roles/save")]
    public Task<ActionResult> SaveUserRoleAsync([FromBody] UserRole r, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SaveUserRoleAsync(r, cancellationToken); return Ok(new { message = "User role saved." }); });

    [HttpPost("project-types/save")]
    public Task<ActionResult> SaveProjectTypeAsync([FromBody] ProjectType pt, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SaveProjectTypeAsync(pt, cancellationToken); return Ok(new { message = "Project type saved." }); });

    [HttpPost("business-units/save")]
    public Task<ActionResult> SaveBusinessUnitAsync([FromBody] BusinessUnit bu, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SaveBusinessUnitAsync(bu, cancellationToken); return Ok(new { message = "Business unit saved." }); });

    [HttpPost("business-units/{businessUnitId:int}/active")]
    public Task<ActionResult> SetBusinessUnitActiveAsync(int businessUnitId, [FromQuery] bool isActive, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SetBusinessUnitActiveAsync(businessUnitId, isActive, cancellationToken); return Ok(new { message = isActive ? "Activated." : "Deactivated." }); });

    [HttpPost("business-units/{businessUnitId:int}/delete")]
    public Task<ActionResult> HardDeleteBusinessUnitAsync(int businessUnitId, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            await _adminService.HardDeleteBusinessUnitAsync(businessUnitId, cancellationToken);
            return Ok(new { message = "Business unit deleted." });
        });

    [HttpPost("business-classes/save")]
    public Task<ActionResult> SaveBusinessClassAsync([FromBody] BusinessClass bc, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SaveBusinessClassAsync(bc, cancellationToken); return Ok(new { message = "Business class saved." }); });

    [HttpPost("business-classes/{businessClassId:int}/delete")]
    public Task<ActionResult> HardDeleteBusinessClassAsync(int businessClassId, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            await _adminService.HardDeleteBusinessClassAsync(businessClassId, cancellationToken);
            return Ok(new { message = "Business class deleted." });
        });

    [HttpPost("client-categories/save")]
    public Task<ActionResult> SaveClientCategoryAsync([FromBody] ClientCategory c, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SaveClientCategoryAsync(c, cancellationToken); return Ok(new { message = "Client category saved." }); });

    [HttpPost("client-types/save")]
    public Task<ActionResult> SaveClientTypeAsync([FromBody] ClientType c, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SaveClientTypeAsync(c, cancellationToken); return Ok(new { message = "Client type saved." }); });

    [HttpPost("client-types/{clientTypeId:int}/active")]
    public Task<ActionResult> SetClientTypeActiveAsync(int clientTypeId, [FromQuery] bool isActive, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SetClientTypeActiveAsync(clientTypeId, isActive, cancellationToken); return Ok(new { message = isActive ? "Activated." : "Deactivated." }); });

    [HttpPost("service-categories/save")]
    public Task<ActionResult> SaveServiceCategoryAsync([FromBody] ServiceCategory c, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SaveServiceCategoryAsync(c, cancellationToken); return Ok(new { message = "Service category saved." }); });

    [HttpPost("service-categories/{serviceCategoryId:int}/active")]
    public Task<ActionResult> SetServiceCategoryActiveAsync(int serviceCategoryId, [FromQuery] bool isActive, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SetServiceCategoryActiveAsync(serviceCategoryId, isActive, cancellationToken); return Ok(new { message = isActive ? "Activated." : "Deactivated." }); });

    [HttpPost("service-groups/save")]
    public Task<ActionResult> SaveServiceGroupAsync([FromBody] ServiceGroup group, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            var id = await _adminService.SaveServiceGroupAsync(group, cancellationToken);
            return Ok(new
            {
                serviceGroupID = id,
                message = group.ServiceGroupID > 0 ? "Service group updated." : "Service group created.",
            });
        });

    [HttpPost("tags/save")]
    public Task<ActionResult> SaveTagAsync([FromBody] Tag t, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SaveTagAsync(t, cancellationToken); return Ok(new { message = "Tag saved." }); });

    // ---- USER GROUPS ---------------------------------------------------

    [HttpGet("groups/next-number")]
    public Task<ActionResult> GetNextUserGroupNumberAsync(CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            var nextUserGroupNumber = await _adminService.GetNextUserGroupNumberAsync(cancellationToken);
            return Ok(new { nextUserGroupNumber });
        });

    [HttpGet("user-groups/search")]
    public async Task<ActionResult<object>> SearchUserGroupsAsync([FromQuery] string? term, CancellationToken cancellationToken)
    {
        try
        {
            var groups = await _adminService.SearchUserGroupsAsync(term, cancellationToken);
            return Ok(new { groups });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin API SearchUserGroups failed.");
            return Problem(detail: UserFacingError(ex), title: "Operation failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("user-groups/{ugid:int}/members")]
    public async Task<ActionResult<object>> ListUserGroupMembersAsync(int ugid, CancellationToken cancellationToken)
    {
        try
        {
            var members = await _adminService.ListUserGroupMembersAsync(ugid, cancellationToken);
            return Ok(new { members });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin API ListUserGroupMembers failed.");
            return Problem(detail: UserFacingError(ex), title: "Operation failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("groups/save")]
    public Task<ActionResult> SaveUserGroupAsync([FromBody] UserGroup body, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            var ugid = await _adminService.SaveUserGroupAsync(body, cancellationToken);
            return Ok(new { ugid, message = "User group saved." });
        });

    [HttpPost("groups/save-batch")]
    public Task<ActionResult> SaveUserGroupBatchAsync([FromBody] SaveUserGroupBatchRequest body, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            var ugid = await _adminService.SaveUserGroupBatchAsync(body, cancellationToken);
            var groups = await _adminService.ListUserGroupsAsync(null, true, cancellationToken);
            var saved = groups.FirstOrDefault(g => g.UGID == ugid);
            return Ok(new { ugid, userGroupNumber = saved?.UserGroupNumber, message = "Group + members saved." });
        });

    [HttpPost("groups/{ugid:int}/soft-delete")]
    public Task<ActionResult> SoftDeleteUserGroupAsync(int ugid, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SoftDeleteUserGroupAsync(ugid, cancellationToken); return Ok(new { message = "Group soft-deleted." }); });

    [HttpPost("groups/{ugid:int}/delete")]
    public Task<ActionResult> HardDeleteUserGroupAsync(int ugid, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            await _adminService.HardDeleteUserGroupAsync(ugid, cancellationToken);
            return Ok(new { message = "User group deleted." });
        });

    [HttpPost("groups/{ugid:int}/restore")]
    public Task<ActionResult> RestoreUserGroupAsync(int ugid, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.RestoreUserGroupAsync(ugid, cancellationToken); return Ok(new { message = "Group restored." }); });

    [HttpPost("groups/members/save")]
    public Task<ActionResult> SaveUserGroupMemberAsync([FromBody] UserGroupMember member, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SaveUserGroupMemberAsync(member, cancellationToken); return Ok(new { message = "Member added." }); });

    [HttpDelete("groups/{ugid:int}/members/{userId:int}")]
    public Task<ActionResult> RemoveUserGroupMemberAsync(int ugid, int userId, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.RemoveUserGroupMemberAsync(ugid, userId, cancellationToken); return Ok(new { message = "Member removed." }); });

    // ---- SERVICES + TAGS -----------------------------------------------

    [HttpPost("services/save")]
    public Task<ActionResult> SaveCatalogServiceAsync([FromBody] Service service, CancellationToken cancellationToken)
        => RunAsync(async () =>
        {
            var id = await _adminService.SaveCatalogServiceAsync(service, cancellationToken);
            return Ok(new { serviceId = id, message = "Service saved." });
        });

    [HttpPost("services/{serviceId:int}/soft-delete")]
    public Task<ActionResult> SoftDeleteCatalogServiceAsync(int serviceId, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.SoftDeleteCatalogServiceAsync(serviceId, cancellationToken); return Ok(new { message = "Service soft-deleted." }); });

    [HttpPost("services/{serviceId:int}/restore")]
    public Task<ActionResult> RestoreCatalogServiceAsync(int serviceId, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.RestoreCatalogServiceAsync(serviceId, cancellationToken); return Ok(new { message = "Service restored." }); });

    [HttpPost("services/{serviceId:int}/tags/{tagId:int}")]
    public Task<ActionResult> AddServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.AddServiceTagAsync(serviceId, tagId, cancellationToken); return Ok(new { message = "Tag attached." }); });

    [HttpDelete("services/{serviceId:int}/tags/{tagId:int}")]
    public Task<ActionResult> RemoveServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken)
        => RunAsync(async () => { await _adminService.RemoveServiceTagAsync(serviceId, tagId, cancellationToken); return Ok(new { message = "Tag detached." }); });

    [HttpPost("services/bulk-import/parse")]
    [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<ServiceBulkParseResult>> ParseServiceBulkImportAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No file", Detail = "Choose a CSV file to import." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await _adminService.ParseServiceImportCsvAsync(stream, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Parse rejected", Detail = ex.Message });
        }
    }

    [HttpPost("services/bulk-import/verify")]
    public async Task<ActionResult<ServiceBulkVerifyResult>> VerifyServiceBulkImportAsync(
        [FromBody] ServiceBulkRowsRequest body, CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        return Ok(await _adminService.VerifyServiceImportRowsAsync(body.Rows, cancellationToken));
    }

    [HttpPost("services/bulk-import/commit")]
    public async Task<ActionResult<ServiceBulkImportResult>> CommitServiceBulkImportAsync(
        [FromBody] ServiceBulkRowsRequest body, CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        try
        {
            return Ok(await _adminService.CommitServiceImportRowsAsync(body.Rows, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Import not allowed", Detail = ex.Message });
        }
    }

    // ---- ACTIVITY ------------------------------------------------------

    [HttpGet("activity")]
    public async Task<ActionResult<IReadOnlyList<ActivityLog>>> GetActivityAsync(
        [FromQuery] string entityType,
        [FromQuery] int entityId,
        [FromQuery] int top = 100,
        CancellationToken cancellationToken = default)
        => Ok(await _adminService.GetActivityForEntityAsync(entityType, entityId, top, cancellationToken));

    // ---- DATA AUDIT ----------------------------------------------------

    [HttpGet("data-audit")]
    public async Task<ActionResult<object>> GetDataAuditAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.GetDataAuditReportAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin API DataAudit failed.");
            return Problem(detail: UserFacingError(ex), title: "Operation failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("data-audit/export")]
    public async Task<ActionResult<object>> GetDataAuditExportAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.GetDataAuditExportAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin API DataAudit export failed.");
            return Problem(detail: UserFacingError(ex), title: "Operation failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private string UserFacingError(Exception ex) =>
        _environment.IsDevelopment()
            ? ex.Message
            : "The operation could not be completed. Please try again or contact support.";

    private async Task<ActionResult> RunAsync(Func<Task<ActionResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Operation rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin API POST failed.");
            return Problem(detail: UserFacingError(ex), title: "Operation failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
