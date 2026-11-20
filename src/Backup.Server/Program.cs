using Backup.App.Execution;
using Backup.Server;
using System.Text.Json.Serialization;
using Backup.Server.Api;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(builder.Configuration.GetValue("Port", 5174)));
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
SystemEndpoints.Map(app);

app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<RunService>().ShutdownAsync().GetAwaiter().GetResult());

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

/// <summary>Lets the tests start the server in memory.</summary>
public partial class Program;
