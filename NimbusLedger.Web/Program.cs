using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Options;
using NimbusLedger.Infrastructure.Storage;
using Serilog;

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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSerilogRequestLogging();

app.UseRouting();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapGet("/api/snapshot", async (ILedgerSnapshotStore store, CancellationToken cancellationToken) =>
{
    var snapshot = await store.GetLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);
    return snapshot is null ? Results.NoContent() : Results.Ok(snapshot);
}).WithName("GetSnapshot");

app.Run();
