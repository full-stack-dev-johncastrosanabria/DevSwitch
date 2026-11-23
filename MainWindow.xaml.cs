using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;

namespace DevSwitch;

public partial class MainWindow : Window
{
    private readonly IReadOnlyList<Switchable> _items = Catalog.Create();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly bool _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        AdminBanner.Visibility = _isAdmin ? Visibility.Collapsed : Visibility.Visible;
        if (_isAdmin) Title += " (administrador)";

        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { await RefreshAsync(); _timer.Start(); };
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            UpdateRam();
            var snap = await Task.Run(Snapshot.TakeAsync);
            foreach (var item in _items) item.Update(snap);
            if (StatusLine.Text == "Leyendo estado…") StatusLine.Text = "Listo. Se actualiza cada 5 segundos.";
        }
        catch (Exception ex)
        {
            StatusLine.Text = "No pude leer el estado: " + ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Switchable item) return;
        if (item.RequiresAdmin && !EnsureAdmin(item)) return;

        var stopping = item.IsRunning;
        if (stopping && item.StopWarning is { } warning &&
            MessageBox.Show(this, warning, item.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunAsync(item, stopping ? "Apagando" : "Encendiendo",
            () => stopping ? item.StopAsync() : item.StartAsync(),
            stopping ? "apagado" : "encendido");
    }

    private async void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Switchable item) return;
        var enable = !item.AutoStart;
        if (!EnsureAdmin(item))
        {
            item.Update(await Task.Run(Snapshot.TakeAsync)); // put the checkbox back
            return;
        }
        await RunAsync(item, "Cambiando el inicio de", () => item.SetAutoStartAsync(enable),
            enable ? "arrancará con Windows" : "ya no arrancará con Windows");
    }

    private async Task RunAsync(Switchable item, string verb, Func<Task> action, string doneText)
    {
        item.IsBusy = true;
        StatusLine.Text = $"{verb} {item.Name}…";
        try
        {
            await action();
            StatusLine.Text = $"{item.Name}: {doneText}.";
        }
        catch (Exception ex)
        {
            StatusLine.Text = $"{item.Name}: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
            await RefreshAsync();
        }
    }

    private bool EnsureAdmin(Switchable item)
    {
        if (_isAdmin) return true;
        StatusLine.Text = $"{item.Name} necesita permisos de administrador: pulsa \"Abrir como administrador\".";
        return false;
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" })?.Dispose();
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            StatusLine.Text = "Se canceló el permiso de administrador.";
        }
    }

    private void UpdateRam()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m)) return;
        double gb = 1L << 30;
        var used = (m.TotalPhys - m.AvailPhys) / gb;
        var total = m.TotalPhys / gb;
        RamBar.Value = m.MemoryLoad;
        RamText.Text = $"{used:0.0} de {total:0.0} GB en uso ({m.MemoryLoad} %) · {m.AvailPhys / gb:0.0} GB libres";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
