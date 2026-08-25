using PineAI.Backup;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "PineAI Backup Service";
    });

    builder.Services.AddSerilog((services, loggerConfig) => loggerConfig
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("AppName", "PineAI.Backup")
        .WriteTo.Console()
        .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"]));

    builder.Services.Configure<BackupSettings>(
        builder.Configuration.GetSection("BackupSettings"));

    builder.Services.AddHttpClient();

    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Backup host terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
