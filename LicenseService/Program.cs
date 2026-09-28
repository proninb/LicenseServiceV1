using LicenseService.Api;
using LicenseService.Components;
using LicenseService.Data;
using LicenseService.Licensing;
using LicenseService.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using System.Net;

var options = new WebApplicationOptions
{
    Args = args,
};

var builder = WebApplication.CreateBuilder(options);

var developmentAuthenticationEnabled =
    builder.Configuration.GetValue<bool>(
        "DevelopmentAuthentication:Enabled");

if (developmentAuthenticationEnabled &&
    !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "DevelopmentAuthentication may be enabled only in the Development environment.");
}

builder.Host.UseWindowsService(service =>
{
    service.ServiceName = "CW License Service";
});

var authentication =
    builder.Services.AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme);

authentication.AddMicrosoftIdentityWebApi(
    builder.Configuration.GetSection("AzureAdApi"),
    jwtBearerScheme:
        JwtBearerDefaults.AuthenticationScheme);

if (developmentAuthenticationEnabled)
{
    authentication.AddScheme<
        AuthenticationSchemeOptions,
        DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationDefaults.Scheme,
            _ =>
            {
            });
}

var requiredRole =
    builder.Configuration["Authorization:RequiredApplicationRole"]
    ?? "ServerEngine.Access";

builder.Services.AddAuthorization(authorization =>
{
    authorization.AddPolicy("ServerEngine", policy =>
    {
        if (developmentAuthenticationEnabled)
        {
            policy.AddAuthenticationSchemes(
                JwtBearerDefaults.AuthenticationScheme,
                DevelopmentAuthenticationDefaults.Scheme);
        }
        else
        {
            policy.AddAuthenticationSchemes(
                JwtBearerDefaults.AuthenticationScheme);
        }

        policy.RequireAuthenticatedUser();
        policy.RequireRole(requiredRole);
    });

    authorization.AddPolicy("LocalAdmin", policy =>
    {
        policy.RequireAssertion(context =>
        {
            if (context.Resource is not HttpContext httpContext)
            {
                return false;
            }

            var remote =
                httpContext.Connection.RemoteIpAddress;

            return remote is not null &&
                   IPAddress.IsLoopback(remote);
        });
    });
});

var connectionString =
    builder.Configuration.GetConnectionString("LicenseDatabase")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:LicenseDatabase is required.");

builder.Services.AddDbContextFactory<LicenseDbContext>(database =>
{
    database.UseSqlServer(connectionString);
});

builder.Services.AddSingleton<LeaseSigner>();
builder.Services.AddScoped<LeaseService>();

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:EnsureCreated"))
{
    await using var scope =
        app.Services.CreateAsyncScope();

    var factory =
        scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<LicenseDbContext>>();

    await using var database =
        await factory.CreateDbContextAsync();

    await database.Database.EnsureCreatedAsync();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapLeaseEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization("LocalAdmin");

app.Run();
