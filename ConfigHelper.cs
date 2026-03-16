namespace Wafek_Web_Manager;

/// <summary>
/// مسار ملف الإعدادات — يدعم القرص الدائم على Render عبر CONFIG_PATH
/// </summary>
public static class ConfigHelper
{
    public static string GetConfigFilePath()
    {
        var configDir = Environment.GetEnvironmentVariable("CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(configDir))
        {
            var dir = configDir.Trim();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.Combine(dir, "appsettings.custom.json");
        }
        var basePath = Path.Combine(AppContext.BaseDirectory, "appsettings.custom.json");
        if (File.Exists(basePath)) return basePath;
        return Path.Combine(Directory.GetCurrentDirectory(), "appsettings.custom.json");
    }
}
