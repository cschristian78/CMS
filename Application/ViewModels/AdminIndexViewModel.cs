using ClientManagementSystem.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ClientManagementSystem.ViewModels;

public sealed class AdminIndexViewModel
{
    public ServerConnectionSettings ServerSettings { get; set; } = new();
    public IReadOnlyList<string> ServerOptions { get; set; } = [];

    public IReadOnlyList<User> Users { get; set; } = [];
    public IReadOnlyList<User> BmsUsers { get; set; } = [];
    public IReadOnlyList<UserRole> UserRoles { get; set; } = [];

    public IReadOnlyList<UserGroup> UserGroups { get; set; } = [];
    public IReadOnlyList<UserGroupSummary> UserGroupSummaries { get; set; } = [];

    public IReadOnlyList<ProjectType> ProjectTypes { get; set; } = [];
    public IReadOnlyList<BusinessUnit> BusinessUnits { get; set; } = [];
    public IReadOnlyList<BusinessClass> BusinessClasses { get; set; } = [];
    public IReadOnlyList<ClientCategory> ClientCategories { get; set; } = [];
    public IReadOnlyList<ClientType> ClientTypes { get; set; } = [];
    public IReadOnlyList<ServiceCategory> ServiceCategories { get; set; } = [];
    public IReadOnlyList<ServiceGroup> ServiceGroups { get; set; } = [];
    public IReadOnlyList<Tag> Tags { get; set; } = [];

    public IReadOnlyList<Status> Statuses { get; set; } = [];
    public IReadOnlyList<Service> Services { get; set; } = [];
    public IReadOnlyList<ClientAdminRow> Clients { get; set; } = [];

    public IReadOnlyList<SelectListItem> StatusOptions => Statuses
        .Select(s => new SelectListItem(s.DisplayName, s.StatusID.ToString()))
        .ToList();

    public IReadOnlyList<SelectListItem> BmsUserOptions => BmsUsers
        .Select(u => new SelectListItem(u.DisplayName, u.UserID.ToString()))
        .ToList();

    public IReadOnlyList<SelectListItem> UserRoleOptions => UserRoles
        .Select(r => new SelectListItem(r.UserRoleName, r.UserRoleID.ToString()))
        .ToList();
}
