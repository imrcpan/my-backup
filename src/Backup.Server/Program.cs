using Backup.App.Execution;
using Backup.Server;
using System.Text.Json.Serialization;
using Backup.Server.Api;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(CommandLine.Normalize(args));
if (!CommandLine.TryGetPort(builder.Configuration["Port"], 1580, out int port, out string? portError))
{
    Console.Error.WriteLine(portError);
    return 2;
}

builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(port));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJson.Default);
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddBackupServices();

WebApplication app = builder.Build();
HostGuard.Use(app);
ErrorMapping.Use(app);

app.MapGet("/api/ping", () => new Pong("ok"));
DeviceEndpoints.Map(app);
GroupEndpoints.Map(app);
SnapshotEndpoints.Map(app);
RunEndpoints.Map(app);
SystemEndpoints.Map(app);

app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<RunService>().ShutdownAsync().GetAwaiter().GetResult());

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();
return 0;

/// <summary>Lets the tests start the server in memory.</summary>
public partial class Program;
