using System.Text.Json;

namespace CSI.OpenBase.Desktop;

internal sealed class DesktopSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string ApplicationDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CSI OpenBase");

    private static string SettingsPath => Path.Combine(ApplicationDataDirectory, "desktop.json");

    public string WorkspaceDirectory { get; set; } = DefaultWorkspaceDirectory();

    internal bool HasPersistedWorkspace { get; private set; }

    public static DesktopSettings Load(AppLog log)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new DesktopSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<DesktopSettings>(json, JsonOptions);
            if (settings is null || string.IsNullOrWhiteSpace(settings.WorkspaceDirectory))
            {
                throw new InvalidDataException("配置文件未包含工作目录。");
            }

            settings.WorkspaceDirectory = Path.GetFullPath(settings.WorkspaceDirectory);
            settings.HasPersistedWorkspace = true;
            return settings;
        }
        catch (Exception exception)
        {
            log.Write("desktop", $"Could not load settings; defaults will be used: {exception.Message}");
            return new DesktopSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        WorkspaceDirectory = Path.GetFullPath(WorkspaceDirectory);

        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
        HasPersistedWorkspace = true;
    }

    private static string DefaultWorkspaceDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            return Path.Combine(documents, "CSI OpenBase");
        }

        return Path.Combine(ApplicationDataDirectory, "data");
    }
}
