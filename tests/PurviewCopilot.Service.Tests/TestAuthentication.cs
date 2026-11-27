using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurviewCopilot.Collaboration;

namespace PurviewCopilot.Service.Tests;

/// <summary>
/// Test-only authentication. The service itself only accepts validated Entra tokens; this scheme is
/// registered by the test host so tenant and role behaviour can be exercised without a tenant.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["X-Test-User"].ToString();

        if (string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("oid", user),
            new("preferred_username", user),
            new(ClaimTypes.Name, user)
        };

        foreach (var role in Request.Headers["X-Test-Roles"].ToString()
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim("roles", role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class ServiceFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"pgc-service-{Guid.NewGuid():N}.db");

    public CollaborationStore Store => Services.GetRequiredService<CollaborationStore>();

    public string DatabasePath => _databasePath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Collaboration:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Collaboration", $"Data Source={_databasePath}");
        builder.UseSetting("Service:RequireHttps", "false");
        builder.UseSetting("Service:BootstrapTenant", "");
        builder.UseSetting("Service:BootstrapAdmin", "");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });
    }

    public Task SeedAsync(string tenantId, string userId, CollaborationRole role) =>
        Store.GrantMemberAsync(tenantId, userId, role, "test");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // The temp directory is disposable.
        }
    }
}
