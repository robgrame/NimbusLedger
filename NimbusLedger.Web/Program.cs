using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Options;
using NimbusLedger.Infrastructure.Storage;
using Serilog;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NimbusLedger.Web;

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
builder.Services.AddRazorPages();
builder.Services.AddOptions<WebAccessOptions>()
    .Bind(builder.Configuration.GetSection("WebAccess"))
    .Validate(options => options.AllowedGroups.Length > 0
        && options.AllowedGroups.All(group => !string.IsNullOrWhiteSpace(group)),
        "At least one authorized Windows group must be configured in WebAccess:AllowedGroups.")
    .ValidateOnStart();
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(context => context.Resource is HttpContext http
            && http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<WebAccessOptions>>()
                .Value.AllowedGroups.Any(context.User.IsInRole))
        .Build();
});
builder.Services.AddHealthChecks().AddCheck<SnapshotHealthCheck>("snapshot", tags: ["ready"]);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSerilogRequestLogging();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapGet("/api/snapshot", async (ILedgerSnapshotStore store, CancellationToken cancellationToken) =>
{
    var snapshot = await store.GetLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);
    return snapshot is null ? Results.NoContent() : Results.Ok(snapshot);
}).WithName("GetSnapshot");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.Run();

public partial class Program;
