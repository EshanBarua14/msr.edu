using MsrEdu.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddMsrEdu();

var app = builder.Build();
app.UseMsrEdu();
app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
