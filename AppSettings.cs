using System.Text.Json;

namespace TrackingTrick;

internal sealed class AppSettings
{
    public int IdleSeconds { get; set; } = 60;
    public int MinIntervalSeconds { get; set; } = 5;
    public int MaxIntervalSeconds { get; set; } = 15;
    public int ActiveMinutes { get; set; } = 10;
    public bool StartImmediately { get; set; }
    public bool AutomationEnabled { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrackingTrick", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
