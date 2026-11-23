using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevSwitch;

/// <summary>Something that uses RAM and can be turned on and off: a Windows service, a container, WSL…</summary>
public abstract class Switchable : INotifyPropertyChanged
{
    protected Switchable(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }
    public string Description { get; }

    /// <summary>True when turning it on/off needs the app to run as administrator.</summary>
    public virtual bool RequiresAdmin => false;
    public virtual bool CanStart => true;
    public virtual bool SupportsAutoStart => false;
    /// <summary>If set, the user must confirm this text before stopping.</summary>
    public virtual string? StopWarning => null;

    private bool _isRunning, _isAvailable = true, _isBusy, _autoStart;
    private long _memoryBytes;
    private string _statusText = "…";

    public bool IsRunning { get => _isRunning; protected set { if (Set(ref _isRunning, value)) Notify(nameof(ActionText), nameof(CanAct)); } }
    public bool IsAvailable { get => _isAvailable; protected set { if (Set(ref _isAvailable, value)) Notify(nameof(CanAct), nameof(CanToggleAutoStart)); } }
    public bool IsBusy { get => _isBusy; set { if (Set(ref _isBusy, value)) Notify(nameof(ActionText), nameof(CanAct), nameof(CanToggleAutoStart)); } }
    public bool AutoStart { get => _autoStart; protected set => Set(ref _autoStart, value); }
    public long MemoryBytes { get => _memoryBytes; protected set { if (Set(ref _memoryBytes, value)) Notify(nameof(MemoryText)); } }
    public string StatusText { get => _statusText; protected set => Set(ref _statusText, value); }

    public string MemoryText => MemoryBytes <= 0 ? "—"
        : MemoryBytes >= 1L << 30 ? $"{MemoryBytes / (double)(1L << 30):0.0} GB"
        : $"{MemoryBytes / (double)(1L << 20):0} MB";

    public string ActionText => IsBusy ? "Espera…" : IsRunning ? "Apagar" : CanStart ? "Encender" : "Apagado";
    public bool CanAct => IsAvailable && !IsBusy && (IsRunning || CanStart);
    public bool CanToggleAutoStart => SupportsAutoStart && IsAvailable && !IsBusy;

    /// <summary>Updates state from a fresh snapshot. Runs on the UI thread, so it must be quick.</summary>
    public abstract void Update(Snapshot snap);
    public abstract Task StartAsync();
    public abstract Task StopAsync();
    public virtual Task SetAutoStartAsync(bool enabled) => Task.CompletedTask;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Notify(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
