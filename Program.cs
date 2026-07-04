using ContentRiskScanner.Services;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<RiskEngineService>();

var app = builder.Build();

// Middleware
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();