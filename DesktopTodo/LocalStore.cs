using System.Text.Json;
using System.IO;

namespace DesktopTodo;

public sealed class AppState
{
    public List<TodoItem> Items { get; set; } = [];
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 410;
    public double Height { get; set; } = 650;
    public bool IsTopmost { get; set; } = true;
}

public static class LocalStore
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTodo");
    private static readonly string FilePath = Path.Combine(Folder, "data.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppState Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath), Options) ?? new()
                : new();
        }
        catch
        {
            var damaged = FilePath + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Move(FilePath, damaged); } catch { }
            return new();
        }
    }

    public static void Save(AppState state)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, FilePath, true);
    }

    public static string DataPath => FilePath;
}
