using System.Text.Json;
using System.Text.Json.Serialization;
using ClientManagementSystem.Data;
using ClientManagementSystem.Security;
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
builder.Services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IDbExecutor, DbExecutor>();

builder.Services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
builder.Services.AddScoped<IClientRepository, ClientRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IAdminRepository, AdminRepository>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IHomeRepository, HomeRepository>();

builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IClientProjectListService, ClientProjectListService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<HomeClientProjectBulkImportService>();
builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddHttpClient();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
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
