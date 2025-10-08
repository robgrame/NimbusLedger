using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Options;
using NimbusLedger.Infrastructure.Storage;
using Serilog;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.Configure<HybridLedgerOptions>(builder.Configuration.GetSection("HybridLedger"));
builder.Services.AddSingleton<ILedgerSnapshotStore, FileLedgerSnapshotStore>();

// Authentication & Authorization (Microsoft Entra ID)
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization(options =>
{
    // Require authenticated users by default
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRazorPages(options =>
{
    // Allow anonymous access to specific pages
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Privacy");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSerilogRequestLogging();

app.UseRouting();

// AuthN/Z middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Sign-in/out helpers
app.MapGet("/signin", async (HttpContext ctx) =>
{
    await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = "/"
    });
}).AllowAnonymous();

app.MapGet("/signout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = "/"
    });
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}).RequireAuthorization();

app.MapGet("/api/snapshot", async (ILedgerSnapshotStore store, CancellationToken cancellationToken) =>
{
    var snapshot = await store.GetLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);
    return snapshot is null ? Results.NoContent() : Results.Ok(snapshot);
}).WithName("GetSnapshot").RequireAuthorization();

app.Run();
