using System.Text.Json;
using System.Text.Json.Serialization;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.Security;
using ClientManagementSystem.Serialization;
using ClientManagementSystem.Services;
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

builder.Services.AddSingleton<ISecureConnectionSettingsStore, SecureConnectionSettingsStore>();
builder.Services.AddSingleton<IUserPhotoStorage>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new UserPhotoFileStorage(env.ContentRootPath);
});
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
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IHomeRepository, HomeRepository>();

builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IProjectService, ClientManagementSystem.Services.ProjectService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IClientProjectListService, ClientProjectListService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<HomeClientProjectBulkImportService>();
builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddHttpClient();

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ICalendarOwnerNotificationService, CalendarOwnerNotificationService>();

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

// Authentication and authorization can be added here when the firm's identity provider is selected.
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "home-error",
    pattern: "Home/Error",
    defaults: new { controller = "Home", action = "Error" });

app.MapFallbackToFile("index.html");

app.Run();
