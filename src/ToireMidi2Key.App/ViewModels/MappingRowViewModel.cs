using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToireMidi2Key;
using CoreKeys = ToireMidi2Key.Keys;

namespace ToireMidi2Key.App.ViewModels;

/// <summary>映射表里的一行：一个 MIDI 音 → 一个电脑按键。</summary>
public partial class MappingRowViewModel : ObservableObject
{
    private readonly Action<MappingRowViewModel>? _onDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteDescription))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string NoteText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string KeyText { get; set; } = "";

    public MappingRowViewModel() { }

    public MappingRowViewModel(string noteText, string keyText, Action<MappingRowViewModel>? onDelete = null)
    {
        NoteText = noteText;
        KeyText = keyText;
        _onDelete = onDelete;
    }

    /// <summary>同时显示两种音名，避免 C3/C4 命名歧义。</summary>
    public string NoteDescription
    {
        get
        {
            try
            {
                int note = NoteName.Parse(NoteText, NoteName.NamingScientific);
                return NoteName.Describe(note);
            }
            catch
            {
                return "音名无效";
            }
        }
    }

    public bool IsValid
    {
        get
        {
            try
            {
                NoteName.Parse(NoteText, NoteName.NamingScientific);
                CoreKeys.Resolve(KeyText);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    [RelayCommand]
    private void Delete() => _onDelete?.Invoke(this);

    /// <summary>用来在 UI 上高亮最近学到的那个音。</summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; set; }
}
