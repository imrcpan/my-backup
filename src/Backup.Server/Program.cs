using Backup.Server.Api;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(builder.Configuration.GetValue("Port", 5174)));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJson.Default));

WebApplication app = builder.Build();
HostGuard.Use(app);
app.MapGet("/api/ping", () => new Pong("ok"));

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

/// <summary>Lets the tests start the server in memory.</summary>
public partial class Program;
