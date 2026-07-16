using ContentRiskScanner.Data;
using Microsoft.EntityFrameworkCore;
using ContentRiskScanner.Services;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<RiskEngineService>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=scanner.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Middleware
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();