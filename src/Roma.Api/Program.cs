using Microsoft.AspNetCore.HttpOverrides;
using Roma.Api.Middleware;
using Roma.Application.Categories;
using Roma.Application.Configuration;
using Roma.Application.Gym;
using Roma.Application.Notifications;
using Roma.Application.Tasks;
using Roma.Domain.Categories;
using Roma.Domain.Gym;
using Roma.Domain.Notifications;
using Roma.Domain.Reminders;
using Roma.Domain.Tasks;
using Roma.Infrastructure.Categories;
using Roma.Infrastructure.Database;
using Roma.Infrastructure.Gym;
using Roma.Infrastructure.Notifications;
using Roma.Infrastructure.Reminders;
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
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CORS_ORIGINS")))
    options.CorsOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS")!;
if (bool.TryParse(Environment.GetEnvironmentVariable("SWAGGER_ENABLED"), out var swaggerEnabled))
    options.SwaggerEnabled = swaggerEnabled;

options.Validate();

builder.Services.AddSingleton(options);

// Reverse Proxy support (Dokploy / Traefik / Nginx)
builder.Services.Configure<ForwardedHeadersOptions>(fho =>
{
    fho.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    fho.KnownIPNetworks.Clear();
    fho.KnownProxies.Clear();
});

// CORS Configuration
builder.Services.AddCors(corsOptions =>
{
    corsOptions.AddDefaultPolicy(policy =>
    {
        if (string.IsNullOrWhiteSpace(options.CorsOrigins) || options.CorsOrigins.Trim() == "*")
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            var origins = options.CorsOrigins
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

// Database services
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory($"Data Source={options.DbPath}"));
builder.Services.AddSingleton<DatabaseService>();

// Repositories
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<IReminderRepository, ReminderRepository>();
builder.Services.AddScoped<IDaySummaryRepository, DaySummaryRepository>();
builder.Services.AddScoped<IWorkoutSessionRepository, WorkoutSessionRepository>();

// Application Services
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<WorkoutSessionService>();

// Notifications & Background Worker
if (options.NotifyEnabled)
{
    builder.Services.AddSingleton<INotifierPort, TelegramNotifier>();
    builder.Services.AddHostedService<NotificationWorker>();
}
else
{
    builder.Services.AddSingleton<INotifierPort, NullNotifier>();
}

// Controllers & OpenAPI
builder.Services.AddControllers();

var isSwaggerActive = options.SwaggerEnabled ?? builder.Environment.IsDevelopment();
if (isSwaggerActive)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

var app = builder.Build();

app.UseForwardedHeaders();

// Security Headers (OWASP Hardening)
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors();

if (isSwaggerActive)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Roma API v1");
        c.RoutePrefix = "swagger";
    });
}

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

if (isSwaggerActive)
{
    app.MapGet("/", () => Results.Redirect("/swagger"));
}
else
{
    app.MapGet("/", () => Results.Ok(new { name = "Roma API", status = "running" }));
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.Run();

public partial class Program { }
