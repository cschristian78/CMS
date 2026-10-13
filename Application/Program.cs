using System.Text.Json;
using System.Text.Json.Serialization;
using ClientManagementSystem.Data;
using ClientManagementSystem.Hubs;
using ClientManagementSystem.Models;
using ClientManagementSystem.Security;
using ClientManagementSystem.Serialization;
using ClientManagementSystem.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appDataPath);
var dataProtectionKeyPath = Path.Combine(appDataPath, "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeyPath);

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
});

builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Converters.Add(new NullableDateTimeJsonConverter());
    });

builder.Services
    .AddDataProtection()
    .SetApplicationName("ClientManagementSystem")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "cms.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddSingleton<ISecureConnectionSettingsStore, SecureConnectionSettingsStore>();
builder.Services.AddSingleton<ICatAiSettingsStore, CatAiSettingsStore>();
builder.Services.AddSingleton<ICorrespondenceSettingsStore, CorrespondenceSettingsStore>();
builder.Services.AddSingleton<IUserPhotoStorage>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new UserPhotoFileStorage(env.ContentRootPath);
});
builder.Services.AddSingleton<IPresenceTracker, PresenceTracker>();
builder.Services.Configure<AzureBlobOptions>(builder.Configuration.GetSection(AzureBlobOptions.SectionName));
builder.Services.AddSingleton<IProjectBlobStorage, AzureProjectBlobStorage>();
builder.Services.AddSingleton<IProjectFileUploadSessionStore, ProjectFileUploadSessionStore>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    // 2 GB file + multipart overhead headroom
    options.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024 + 100L * 1024 * 1024;
    options.ValueLengthLimit = int.MaxValue;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 2L * 1024 * 1024 * 1024 + 100L * 1024 * 1024;
});
builder.Services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IDbExecutor, DbExecutor>();

builder.Services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
builder.Services.AddScoped<IClientRepository, ClientRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IAdminRepository, AdminRepository>();
builder.Services.AddScoped<IDataAuditRepository, DataAuditRepository>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IHomeRepository, HomeRepository>();
builder.Services.AddScoped<ITmsRepository, TmsRepository>();
builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddScoped<ICatAiConversationRepository, CatAiConversationRepository>();

builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IProjectService, ClientManagementSystem.Services.ProjectService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IClientProjectListService, ClientProjectListService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ITmsService, TmsService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<HomeClientProjectBulkImportService>();
builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<ICatAiSettingsResolver, CatAiSettingsResolver>();
builder.Services.AddScoped<IProjectReportService, ProjectReportService>();
builder.Services.AddScoped<ICatAiToolService, CatAiToolService>();
builder.Services.AddScoped<ICatAiChatService, CatAiChatService>();
builder.Services.AddHttpClient();

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ICalendarOwnerNotificationService, CalendarOwnerNotificationService>();
builder.Services.AddScoped<IFileNoteNotificationService, FileNoteNotificationService>();
builder.Services.AddSingleton<IRealtimeNotifier, RealtimeNotifier>();
builder.Services.AddSingleton<IProjectAnalysisProfileStore, FileProjectAnalysisProfileStore>();
builder.Services.AddScoped<IProjectAnalysisProfileService, ProjectAnalysisProfileService>();
builder.Services.AddSignalR(options =>
{
    // Small client messages (join/leave). Leave transport selection on the default
    // so Server-Sent Events and long polling work until IIS WebSockets are enabled.
    options.MaximumReceiveMessageSize = 32 * 1024;
});

var app = builder.Build();

// Always handle unhandled exceptions (JSON ProblemDetails for /api) so a bad
// endpoint cannot take down page navigation with an HTML developer crash page.
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var ex = feature?.Error;
        var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("UnhandledException");
        logger?.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Operation failed",
                Detail = app.Environment.IsDevelopment()
                    ? ex?.GetBaseException().Message
                    : "An unexpected error occurred."
            });
            return;
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("An unexpected error occurred.");
    });
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<UpdatesHub>("/hubs/updates");

app.MapControllerRoute(
    name: "home-error",
    pattern: "Home/Error",
    defaults: new { controller = "Home", action = "Error" });

// SPA fallback must not swallow unknown /api routes (those should stay JSON 404s).
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Not Found",
            Detail = $"No API endpoint matches {context.Request.Path}."
        });
        return;
    }

    var indexPath = Path.Combine(app.Environment.WebRootPath, "index.html");
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

try
{
    using var scope = app.Services.CreateScope();
    var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
    auth.EnsureSchemaAsync().GetAwaiter().GetResult();
    var admin = scope.ServiceProvider.GetRequiredService<IAdminRepository>();
    admin.EnsureUserTypeOptionsAsync().GetAwaiter().GetResult();
    var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
    projects.EnsureProjectNoteSchemaAsync().GetAwaiter().GetResult();
    projects.EnsureProjectCalendarSchemaAsync().GetAwaiter().GetResult();
    projects.EnsureProjectFeeBasedRevenueSchemaAsync().GetAwaiter().GetResult();
    projects.EnsureProjectFileCategorySchemaAsync().GetAwaiter().GetResult();
    projects.EnsureProjectFileApprovalSchemaAsync().GetAwaiter().GetResult();
    var catAiConversations = scope.ServiceProvider.GetRequiredService<ICatAiConversationRepository>();
    catAiConversations.EnsureSchemaAsync().GetAwaiter().GetResult();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogWarning(ex, "Could not ensure login/user-type/note/calendar/fee-based/project-file/approval/CatAI schema at startup. It will retry on first use.");
}

app.Run();
