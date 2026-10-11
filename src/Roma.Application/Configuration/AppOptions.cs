namespace Roma.Application.Configuration;

public class AppOptions
{
    public const string SectionName = "Roma";

    public string AppTz { get; set; } = "America/Argentina/Buenos_Aires";
    public bool NotifyEnabled { get; set; } = false;
    public string? TelegramBotToken { get; set; }
    public string? TelegramChatId { get; set; }
    public string EodTime { get; set; } = "23:55";
    public string Categories { get; set; } = "Trabajo,Estudio,Salud,Personal,Hogar,Otros";
    public string DbPath { get; set; } = "./data/roma.db";
    public string EventReminderOffsets { get; set; } = "-7d,-3d,-1d,-2h,0";
    public string CorsOrigins { get; set; } = "*";
    public bool? SwaggerEnabled { get; set; } = null;

    public void Validate()
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(AppTz);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException($"Configured APP_TZ '{AppTz}' is not a valid system timezone ID.");
        }

        if (NotifyEnabled)
        {
            if (string.IsNullOrWhiteSpace(TelegramBotToken))
                throw new InvalidOperationException("TELEGRAM_BOT_TOKEN must be specified when NOTIFY_ENABLED is true.");

            if (string.IsNullOrWhiteSpace(TelegramChatId))
                throw new InvalidOperationException("TELEGRAM_CHAT_ID must be specified when NOTIFY_ENABLED is true.");
        }
    }
}
