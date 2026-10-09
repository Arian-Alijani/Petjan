var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

// Exposed for WebApplicationFactory<Program> in Animora.Server.Tests.
public partial class Program;
