using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Collaboration;
using PurviewCopilot.Service;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.Host.UseWindowsService(options =>
        options.ServiceName = "Purview Governance Copilot Collaboration");
}

var provider = builder.Configuration["Collaboration:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Collaboration");

if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
        ? string.Empty
        : $"Data Source={ServiceDefaults.DefaultDatabasePath(builder.Configuration)}";
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "A SQL Server connection string is required. Set ConnectionStrings:Collaboration.");
}

builder.Services.AddDbContextFactory<CollaborationDbContext>(options =>
    CollaborationStoreFactory.Configure(options, provider, connectionString));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(serviceProvider => new CollaborationStore(
    serviceProvider.GetRequiredService<IDbContextFactory<CollaborationDbContext>>(),
    serviceProvider.GetRequiredService<IClock>(),
    ServiceDefaults.ReadLimits(builder.Configuration)));
builder.Services.AddSingleton<TenantAuthorizationService>();

var authority = builder.Configuration["AzureAd:Authority"];
var audience = builder.Configuration["AzureAd:Audience"];

if (!builder.Environment.IsEnvironment("Testing"))
{
    if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
    {
        // Fail closed: an unauthenticated collaboration service would expose every tenant.
        throw new InvalidOperationException(
            "Entra authentication is not configured. Set AzureAd:Authority and AzureAd:Audience, or run in the Testing environment with a test authentication scheme.");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.RequireHttpsMetadata = true;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RoleClaimType = "roles",
                NameClaimType = "preferred_username",
                ClockSkew = TimeSpan.FromMinutes(2)
            };
        });
}
else
{
    // The test host registers and selects its own scheme after this point.
    builder.Services.AddAuthentication();
}

builder.Services.AddAuthorization();

var httpsRequired = ServiceDefaults.HttpsRequired(builder.Configuration, builder.Environment);

var configuredUrls = builder.Configuration["ASPNETCORE_URLS"] ?? string.Empty;

if (httpsRequired && !configuredUrls.Contains("https://", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "HTTPS is required. Configure an https:// URL (or Kestrel certificate settings), or set Service:RequireHttps=false only for an isolated lab.");
}

var app = builder.Build();

await app.Services.GetRequiredService<CollaborationStore>().InitializeAsync();
await BootstrapFirstAdministratorAsync(app);

if (httpsRequired)
{
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (httpsRequired)
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "HTTPS is required for this service." });
            return;
        }

        await next();
    });
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

    var (status, title) = exception switch
    {
        TenantAccessDeniedException => (StatusCodes.Status403Forbidden, "You do not have access to this tenant."),
        InsufficientRoleException => (StatusCodes.Status403Forbidden, "Your role does not allow this operation."),
        ConcurrencyConflictException => (StatusCodes.Status409Conflict, "The record changed. Reload and retry."),
        ApprovalRuleException => (StatusCodes.Status409Conflict, "The approval rules refused this decision."),
        CollaborationValidationException => (StatusCodes.Status400BadRequest, "The request failed validation."),
        CollaborationNotFoundException => (StatusCodes.Status404NotFound, "The requested record was not found."),
        _ => (StatusCodes.Status500InternalServerError, "The service could not complete the request.")
    };

    if (status >= 500)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Collaboration")
            .LogError(exception, "Unhandled collaboration service failure");
    }

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new
    {
        error = title,
        detail = status >= 500 ? null : exception?.Message
    });
}));

app.UseAuthentication();
app.UseAuthorization();

app.MapCollaborationApi();

app.Run();

static async Task BootstrapFirstAdministratorAsync(WebApplication app)
{
    var tenant = app.Configuration["Service:BootstrapTenant"];
    var administrator = app.Configuration["Service:BootstrapAdmin"];

    if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(administrator))
    {
        return;
    }

    var store = app.Services.GetRequiredService<CollaborationStore>();
    var members = await store.ListMembersAsync(tenant);

    if (members.Count > 0)
    {
        return;
    }

    await store.GrantMemberAsync(tenant, administrator, CollaborationRole.ApplicationAdministrator, "bootstrap");

    app.Logger.LogWarning(
        "Bootstrap administrator '{Admin}' granted Application Administrator on tenant '{Tenant}'. Remove Service:BootstrapAdmin from configuration now.",
        administrator, tenant);
}

/// <summary>Entry point marker for WebApplicationFactory in the test suite.</summary>
public partial class Program;
