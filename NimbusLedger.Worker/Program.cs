using NimbusLedger.Infrastructure;
using NimbusLedger.Worker;
using Serilog;
using NimbusLedger.Sccm;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
	.ReadFrom.Configuration(builder.Configuration)
	.Enrich.FromLogContext()
	.CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(dispose: true);

builder.Services.AddHybridLedgerInfrastructure(builder.Configuration);
builder.Services.AddHostedService<LedgerWorker>();

// Register optional SCCM cleanup services; scheduling may be triggered from LedgerWorker or future worker
builder.Services.AddSccmCleanup(builder.Configuration);

var host = builder.Build();

try
{
	await host.RunAsync().ConfigureAwait(false);
}
finally
{
	Log.CloseAndFlush();
}
