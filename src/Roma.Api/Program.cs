using Roma.Api.Middleware;
using Roma.Application.Categories;
using Roma.Application.Configuration;
using Roma.Application.Tasks;
using Roma.Domain.Categories;
using Roma.Domain.Tasks;
using Roma.Infrastructure.Categories;
using Roma.Infrastructure.Database;
using Roma.Infrastructure.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Configure and validate AppOptions
var options = builder.Configuration.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_TZ")))
    options.AppTz = Environment.GetEnvironmentVariable("APP_TZ")!;
if (bool.TryParse(Environment.GetEnvironmentVariable("NOTIFY_ENABLED"), out var notifyEnabled))
    options.NotifyEnabled = notifyEnabled;
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")))
    options.TelegramBotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID")))
    options.TelegramChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EOD_TIME")))
    options.EodTime = Environment.GetEnvironmentVariable("EOD_TIME")!;
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATEGORIES")))
    options.Categories = Environment.GetEnvironmentVariable("CATEGORIES")!;
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DB_PATH")))
    options.DbPath = Environment.GetEnvironmentVariable("DB_PATH")!;
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EVENT_REMINDER_OFFSETS")))
    options.EventReminderOffsets = Environment.GetEnvironmentVariable("EVENT_REMINDER_OFFSETS")!;

options.Validate();

builder.Services.AddSingleton(options);

// Database services
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory($"Data Source={options.DbPath}"));
builder.Services.AddSingleton<DatabaseService>();

// Repositories
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();

// Application Services
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<TaskService>();

// Controllers
builder.Services.AddControllers();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Ensure db directory exists and initialize database
var dbDir = Path.GetDirectoryName(Path.GetFullPath(options.DbPath));
if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

using (var scope = app.Services.CreateScope())
{
    var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    await dbService.InitializeAsync();
}

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.Run();

public partial class Program { }
