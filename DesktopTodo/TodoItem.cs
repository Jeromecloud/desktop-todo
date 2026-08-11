using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DesktopTodo;

public sealed class TodoItem : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string _title = "";
    private bool _isCompleted;
    private DateTime? _dueAt;
    private bool _reminderShown;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [JsonIgnore]
    public bool IsDraft { get; set; }

    public string Title { get => _title; set { _title = value; OnChanged(); } }
    public bool IsCompleted { get => _isCompleted; set { _isCompleted = value; OnChanged(); OnChanged(nameof(TitleOpacity)); } }
    public DateTime? DueAt { get => _dueAt; set { _dueAt = value; _reminderShown = false; OnChanged(); OnChanged(nameof(DueText)); } }
    public bool ReminderShown { get => _reminderShown; set { _reminderShown = value; OnChanged(); } }
    public double TitleOpacity => IsCompleted ? 0.42 : 1;
    public string DueText => DueAt switch
    {
        null => "",
        var date when date.Value.Date == DateTime.Today => $"今天 {date:HH:mm}",
        var date when date.Value.Date == DateTime.Today.AddDays(1) => $"明天 {date:HH:mm}",
        var date => date.Value.ToString("M月d日 HH:mm")
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
