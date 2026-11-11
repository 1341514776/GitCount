using System.Text.Encodings.Web;
using System.Text.Json;
using System.IO;
using GitCount.Models;

namespace GitCount.Services;

public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ProjectStore(string? stateFilePath = null)
    {
        StateFilePath = stateFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitCount",
            "state.json");
    }

    public string StateFilePath { get; }

    public ProjectState Load()
    {
        if (!File.Exists(StateFilePath))
        {
            return new ProjectState();
        }

        try
        {
            var json = File.ReadAllText(StateFilePath);
            return JsonSerializer.Deserialize<ProjectState>(json, JsonOptions) ?? new ProjectState();
        }
        catch
        {
            return new ProjectState();
        }
    }

    public void Save(ProjectState state)
    {
        var directory = Path.GetDirectoryName(StateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = StateFilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(tempPath, StateFilePath, true);
    }
}
