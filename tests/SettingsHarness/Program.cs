using System.Text.Json;
using CSI.OpenBase.Desktop;

var scenario = args[0];
var root = Path.GetFullPath(args[1]);
var settingsPath = Path.Combine(root, "settings", "desktop.json");
var workspace = Path.Combine(root, "Documents", "CSI OpenBase");
var log = new AppLog();

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void SeedSettings(string directory)
{
    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
    File.WriteAllText(settingsPath, JsonSerializer.Serialize(new { WorkspaceDirectory = directory }));
}

switch (scenario)
{
    case "first-run":
    {
        var settings = DesktopSettings.Load(log, settingsPath);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var expectedDefault = string.IsNullOrWhiteSpace(documents)
            ? Path.Combine(DesktopSettings.ApplicationDataDirectory, "data")
            : Path.Combine(documents, "CSI OpenBase");
        Check(settings.WorkspaceDirectory == expectedDefault, "Unexpected default directory");
        Check(!settings.HasPersistedWorkspace, "Missing settings were treated as saved");
        // Keep filesystem effects in this test's temporary workspace.
        settings.WorkspaceDirectory = workspace;
        settings.EnsureWorkspace();
        Check(Directory.Exists(workspace), "Workspace was not created");
        Check(settings.HasPersistedWorkspace, "Workspace was not persisted");
        var reloaded = DesktopSettings.Load(log, settingsPath);
        Check(reloaded.HasPersistedWorkspace && reloaded.WorkspaceDirectory == workspace,
            "The next launch did not reuse the saved workspace");
        Check(!File.Exists(settingsPath + ".tmp"), "Atomic save left a temporary file");
        break;
    }
    case "saved-workspace":
    {
        Directory.CreateDirectory(workspace);
        var sentinel = Path.Combine(workspace, "user-data.txt");
        File.WriteAllText(sentinel, "preserve");
        SeedSettings(workspace);
        var original = File.ReadAllText(settingsPath);
        var settings = DesktopSettings.Load(log, settingsPath);
        settings.EnsureWorkspace();
        Check(settings.HasPersistedWorkspace && settings.WorkspaceDirectory == workspace,
            "Existing workspace selection was replaced");
        Check(File.ReadAllText(settingsPath) == original, "Saved preferences were rewritten");
        Check(File.ReadAllText(sentinel) == "preserve", "Existing data was changed");
        break;
    }
    case "change-workspace":
    {
        SeedSettings(workspace);
        var settings = DesktopSettings.Load(log, settingsPath);
        settings.EnsureWorkspace();
        var sentinel = Path.Combine(workspace, "user-data.txt");
        File.WriteAllText(sentinel, "preserve");
        var custom = Path.Combine(root, "用户自选目录");
        settings.WorkspaceDirectory = custom;
        settings.EnsureWorkspace();
        settings.Save();
        Check(DesktopSettings.Load(log, settingsPath).WorkspaceDirectory == custom,
            "Custom workspace was not remembered");
        Check(Directory.Exists(custom), "Custom workspace was not created");
        Check(File.ReadAllText(sentinel) == "preserve", "Previous workspace data was moved");
        break;
    }
    case "unavailable-workspace":
    {
        File.WriteAllText(workspace = Path.Combine(root, "occupied"), "preserve");
        var settings = DesktopSettings.Load(log, settingsPath);
        settings.WorkspaceDirectory = workspace;
        try
        {
            settings.EnsureWorkspace();
            throw new InvalidOperationException("An occupied path was accepted as a workspace");
        }
        catch (IOException) { }
        Check(!settings.HasPersistedWorkspace && !File.Exists(settingsPath),
            "Failed workspace initialization saved an unusable default");
        Check(File.ReadAllText(workspace) == "preserve", "Occupied file was overwritten");
        break;
    }
    case "invalid-settings":
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{invalid");
        var settings = DesktopSettings.Load(log, settingsPath);
        Check(!settings.HasPersistedWorkspace && log.Messages.Count > 0,
            "Invalid configuration was not reported");
        settings.WorkspaceDirectory = workspace;
        settings.EnsureWorkspace();
        Check(DesktopSettings.Load(log, settingsPath).WorkspaceDirectory == workspace,
            "Recovered settings were written to the wrong file");
        break;
    }
    default:
        throw new ArgumentException("Unknown scenario: " + scenario);
}

Console.WriteLine(scenario + ": passed");

namespace CSI.OpenBase.Desktop
{
    internal sealed class AppLog
    {
        public List<string> Messages { get; } = [];
        public void Write(string source, string message) => Messages.Add(message);
    }
}
