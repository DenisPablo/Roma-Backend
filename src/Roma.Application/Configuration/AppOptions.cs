namespace Roma.Application.Configuration;

public class AppOptions
{
    public const string SectionName = "Roma";

    public string AppTz { get; set; } = "America/Argentina/Buenos_Aires";
    public bool NotifyEnabled { get; set; } = false;
    public string? NtfyTopic { get; set; }
    public string NtfyServerUrl { get; set; } = "https://ntfy.sh";
    public string? NtfyToken { get; set; }
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
            var hasNtfy = !string.IsNullOrWhiteSpace(NtfyTopic);
            var hasTelegram = !string.IsNullOrWhiteSpace(TelegramBotToken) && !string.IsNullOrWhiteSpace(TelegramChatId);

            if (!hasNtfy && !hasTelegram)
            {
                throw new InvalidOperationException("Either NTFY_TOPIC or TELEGRAM_BOT_TOKEN/TELEGRAM_CHAT_ID must be specified when NOTIFY_ENABLED is true.");
            }
        }
    }
}
