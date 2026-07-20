using ContentRiskScanner.Data;
using ContentRiskScanner.Services;
using Microsoft.EntityFrameworkCore;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 150_000_000;
});

// Add services
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<RiskEngineService>();        // NLU: text-based harm detection
builder.Services.AddHttpClient<SpeechToTextService>();      // STT: voice/audio ko text me convert karne ke liye
builder.Services.AddHttpClient<TextToSpeechService>();      // TTS: text ko audio me convert karne ke liye
builder.Services.AddHttpClient<ImageDetectionService>();    // Image analysis: watsonx.ai Granite Vision se image scan
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=scanner.db"));

var app = builder.Build();

// Database create karo agar exist nahi karti
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// FFmpeg download karo aur path explicitly set karo — audio extraction ke liye zaroori
string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "FFmpeg");
Directory.CreateDirectory(ffmpegPath);
FFmpeg.SetExecutablesPath(ffmpegPath);
await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);

// Middleware
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();