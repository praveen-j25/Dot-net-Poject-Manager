using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using TaskManagerMVC.Authorization;
using TaskManagerMVC.Data;
using TaskManagerMVC.Security;
using TaskManagerMVC.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

// Anti-Forgery Token Configuration
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "X-CSRF-TOKEN";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Database - ADO.NET
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Support for Render environment variables
var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
if (string.IsNullOrWhiteSpace(dbHost))
{
    dbHost = "taskmanager-db"; // Fallback to Render internal network DNS name
}

var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
var dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "task_manager_db";
var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "root";
var dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "ChangeMe123!";

connectionString = $"Server={dbHost};Port={dbPort};Database={dbName};User={dbUser};Password={dbPass};SslMode=None;AllowPublicKeyRetrieval=true;";
Console.WriteLine($"[INIT] Configured to connect to Database at {dbHost}:{dbPort}");

builder.Services.AddSingleton(new DbConnectionFactory(connectionString!));
builder.Services.AddTransient<DatabaseSeeder>();


// Session Configuration
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Authentication - Session-based with Cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(24);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Authorization Policies
builder.Services.AddAuthorization(options =>
{
    // Role-based policies
    options.AddPolicy(Policies.AdminOnly, policy =>
        policy.Requirements.Add(new RoleRequirement(Roles.Admin)));
    
    options.AddPolicy(Policies.ManagerOrAbove, policy =>
        policy.Requirements.Add(new RoleRequirement(Roles.Admin, Roles.Manager)));
    
    options.AddPolicy(Policies.ManagerOnly, policy =>
        policy.Requirements.Add(new RoleRequirement(Roles.Manager)));
    
    options.AddPolicy(Policies.EmployeeOnly, policy =>
        policy.Requirements.Add(new RoleRequirement(Roles.Employee)));
    
    options.AddPolicy(Policies.AllAuthenticated, policy =>
        policy.RequireAuthenticatedUser());
    
    // Permission-based policies
    options.AddPolicy(Policies.ManageUsers, policy =>
        policy.Requirements.Add(new PermissionRequirement("users.manage")));
    
    options.AddPolicy(Policies.ViewUsers, policy =>
        policy.Requirements.Add(new PermissionRequirement("users.view")));
    
    options.AddPolicy(Policies.ManageProjects, policy =>
        policy.Requirements.Add(new PermissionRequirement("projects.manage")));
    
    options.AddPolicy(Policies.ViewProjects, policy =>
        policy.Requirements.Add(new PermissionRequirement("projects.view")));
    
    options.AddPolicy(Policies.AssignTasks, policy =>
        policy.Requirements.Add(new PermissionRequirement("tasks.assign")));
    
    options.AddPolicy(Policies.ManageAllTasks, policy =>
        policy.Requirements.Add(new PermissionRequirement("tasks.manage_all")));
    
    options.AddPolicy(Policies.ManageOwnTasks, policy =>
        policy.Requirements.Add(new PermissionRequirement("tasks.manage_own")));
    
    options.AddPolicy(Policies.ViewReports, policy =>
        policy.Requirements.Add(new PermissionRequirement("reports.view")));
    
    options.AddPolicy(Policies.ExportReports, policy =>
        policy.Requirements.Add(new PermissionRequirement("reports.export")));
    
    options.AddPolicy(Policies.ManageSettings, policy =>
        policy.Requirements.Add(new PermissionRequirement("settings.manage")));
});

// Authorization Handlers
builder.Services.AddSingleton<IAuthorizationHandler, RoleRequirementHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionRequirementHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, TaskOwnerRequirementHandler>();

// Services
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<CommentService>();
builder.Services.AddScoped<ActivityLogService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<PendingUserService>();
builder.Services.AddScoped<ManagerService>();

var app = builder.Build();

// Security Headers Middleware
app.UseSecurityHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Run migrations in the background so it doesn't block Kestrel from binding to the port (fixes Render timeout)
_ = Task.Run(() => 
{
    try
    {
        using (var scope = app.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            seeder.Initialize();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[BACKGROUND ERROR] Seeder failed: {ex.Message}");
    }
});

app.UseStaticFiles();
app.UseRouting();
app.UseSession(); // Session must be before Authentication
app.UseAuthentication();
app.UseAuthorization();

// Route for controller-only URLs (defaults action to Index)
app.MapControllerRoute(
    name: "controller-only",
    pattern: "{controller}/{action=Index}/{id?}");

// Default route (for root URL /Account/Login)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
