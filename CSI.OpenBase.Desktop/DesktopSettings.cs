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

    private string _settingsPath = SettingsPath;

    public string WorkspaceDirectory { get; set; } = DefaultWorkspaceDirectory();

    internal bool HasPersistedWorkspace { get; private set; }

    public static DesktopSettings Load(AppLog log)
    {
        return Load(log, SettingsPath);
    }

    internal static DesktopSettings Load(AppLog log, string settingsPath)
    {
        settingsPath = Path.GetFullPath(settingsPath);
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new DesktopSettings { _settingsPath = settingsPath };
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<DesktopSettings>(json, JsonOptions);
            if (settings is null || string.IsNullOrWhiteSpace(settings.WorkspaceDirectory))
            {
                throw new InvalidDataException("配置文件未包含工作目录。");
            }

            settings.WorkspaceDirectory = Path.GetFullPath(settings.WorkspaceDirectory);
            settings._settingsPath = settingsPath;
            settings.HasPersistedWorkspace = true;
            return settings;
        }
        catch (Exception exception)
        {
            log.Write("desktop", $"Could not load settings; defaults will be used: {exception.Message}");
            return new DesktopSettings { _settingsPath = settingsPath };
        }
    }

    public void EnsureWorkspace()
    {
        Directory.CreateDirectory(WorkspaceDirectory);
        if (!HasPersistedWorkspace)
        {
            Save();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        WorkspaceDirectory = Path.GetFullPath(WorkspaceDirectory);

        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
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
