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
builder.Services.AddHttpClient<RiskEngineService>();        // Watson NLU: sentiment + emotion + entity detection
builder.Services.AddHttpClient<SpeechToTextService>();      // Watson Speech to Text
builder.Services.AddHttpClient<TextToSpeechService>();      // Watson Text to Speech
builder.Services.AddHttpClient<ImageDetectionService>();    // watsonx.ai Granite Vision (thread-safe IAM token)

var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "DB_CONNECTION_STRING missing. Add it to your .env file: " +
        "DB_CONNECTION_STRING=Host=...;Database=...;Username=...;Password=...");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

// Database create karo agar exist nahi karti
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// FFmpeg path set karo — system-installed binary prefer karein, fallback to bundled directory
var systemFfmpeg = Environment.GetEnvironmentVariable("FFMPEG_EXECUTABLE_PATH");
if (!string.IsNullOrEmpty(systemFfmpeg) && File.Exists(systemFfmpeg))
{
    FFmpeg.SetExecutablesPath(Path.GetDirectoryName(systemFfmpeg)!);
}
else
{
    var systemPaths = new[] { "/usr/bin", "/usr/local/bin", "/bin" };
    var found = systemPaths.FirstOrDefault(p => File.Exists(Path.Combine(p, "ffmpeg")));
    if (found != null)
    {
        FFmpeg.SetExecutablesPath(found);
    }
    else
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "FFmpeg");
        Directory.CreateDirectory(ffmpegPath);
        FFmpeg.SetExecutablesPath(ffmpegPath);
        try
        {
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: FFmpeg download failed ({ex.Message}). Video audio extraction may be unavailable.");
        }
    }
}

// Middleware
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
