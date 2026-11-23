using System.Diagnostics;
using System.ServiceProcess;

namespace DevSwitch;

/// <summary>
/// A set of Windows services handled as one switch. <paramref name="core"/> services are started and
/// stopped; <paramref name="extras"/> (telemetry, launchers…) are only stopped, never started.
/// The first core service decides whether the switch shows as on.
/// </summary>
public sealed class ServiceGroup(string name, string description, string[] core, string[] extras)
    : Switchable(name, description)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(90);
    private IEnumerable<string> All => core.Concat(extras);

    public override bool RequiresAdmin => true;
    public override bool SupportsAutoStart => true;

    public override void Update(Snapshot snap)
    {
        var main = snap.Services.GetValueOrDefault(core[0]);
        IsAvailable = main != null;
        if (main == null)
        {
            IsRunning = false;
            MemoryBytes = 0;
            StatusText = "No está instalado";
            return;
        }

        IsRunning = main.State == "Running";
        AutoStart = main.StartMode == "Auto";
        MemoryBytes = All.Select(n => snap.Services.GetValueOrDefault(n))
            .Where(s => s is { State: "Running", ProcessId: > 0 })
            .Select(s => s!.ProcessId)
            .Distinct()
            .Sum(pid => snap.WorkingSetByPid.GetValueOrDefault(pid));

        var running = All.Count(n => snap.Services.GetValueOrDefault(n)?.State == "Running");
        var state = main.State switch
        {
            "Running" => "Encendido",
            "Stopped" => "Apagado",
            "Start Pending" => "Encendiendo…",
            "Stop Pending" => "Apagando…",
            var other => other,
        };
        StatusText = $"{state} · {running} servicio(s) activos · inicio {(AutoStart ? "automático" : "manual")}";
    }

    public override Task StartAsync() => Task.Run(() =>
    {
        foreach (var n in core)
        {
            using var sc = new ServiceController(n);
            if (!Exists(sc) || sc.StartType == ServiceStartMode.Disabled) continue;
            if (sc.Status == ServiceControllerStatus.Running) continue;
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, Wait);
        }
    });

    public override Task StopAsync() => Task.Run(() =>
    {
        foreach (var n in All.Reverse())
        {
            using var sc = new ServiceController(n);
            if (!Exists(sc) || sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) continue;
            if (!sc.CanStop) continue;
            sc.Stop(stopDependentServices: true);
            sc.WaitForStatus(ServiceControllerStatus.Stopped, Wait);
        }
    });

    /// <summary>On: core services start with Windows. Off: everything becomes manual, so no RAM is used at boot.</summary>
    public override async Task SetAutoStartAsync(bool enabled)
    {
        foreach (var n in All)
        {
            using var sc = new ServiceController(n);
            if (!Exists(sc) || sc.StartType == ServiceStartMode.Disabled) continue;
            var mode = enabled && core.Contains(n) ? "auto" : "demand";
            await Proc.RunOrThrowAsync("sc.exe", ["config", n, "start=", mode]);
        }
    }

    private static bool Exists(ServiceController sc)
    {
        try { _ = sc.Status; return true; }
        catch (InvalidOperationException) { return false; }
    }
}

public sealed class DockerDesktopSwitch() : Switchable("Docker Desktop",
    "Motor de contenedores. Su máquina virtual se cuenta en la tarjeta de WSL.")
{
    private static readonly string[] Processes = ["Docker Desktop", "com.docker.backend", "com.docker.build"];

    public override void Update(Snapshot snap)
    {
        var exe = FindExe();
        IsAvailable = exe != null;
        IsRunning = snap.HasProcess(Processes);
        MemoryBytes = snap.MemoryOf(Processes);
        StatusText = exe == null ? "No está instalado"
            : !IsRunning ? "Apagado"
            : snap.DockerUp ? "Encendido · motor listo"
            : "Arrancando el motor…";
    }

    public override Task StartAsync()
    {
        var exe = FindExe() ?? throw new InvalidOperationException("No encontré Docker Desktop.");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })?.Dispose();
        return Task.CompletedTask;
    }

    public override Task StopAsync() => Proc.RunOrThrowAsync("docker", ["desktop", "stop"], timeoutMs: 180_000);

    private static string? FindExe() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DockerDesktop", "Docker Desktop.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe"),
    }.FirstOrDefault(File.Exists);
}

/// <summary>One service of C:\dev\databases\compose.yaml, started through its profile.</summary>
public sealed class ComposeSwitch(string name, string description, string service) : Switchable(name, description)
{
    private bool _dockerUp;

    public override bool CanStart => _dockerUp;

    public override void Update(Snapshot snap)
    {
        _dockerUp = snap.DockerUp;
        Notify(nameof(CanStart), nameof(ActionText), nameof(CanAct));
        IsAvailable = File.Exists(Catalog.ComposeFile);
        IsRunning = snap.ComposeMemory.ContainsKey(service);
        MemoryBytes = snap.ComposeMemory.GetValueOrDefault(service);
        StatusText = !IsAvailable ? $"No encontré {Catalog.ComposeFile}"
            : IsRunning ? "Encendido"
            : snap.DockerUp ? "Apagado"
            : "Apagado · enciende Docker Desktop primero";
    }

    // The first start can pull a multi-GB image, hence the long timeout.
    public override Task StartAsync() => Proc.RunOrThrowAsync("docker",
        ["compose", "-f", Catalog.ComposeFile, "--profile", service, "up", "-d", service], timeoutMs: 30 * 60_000);

    public override Task StopAsync() => Proc.RunOrThrowAsync("docker",
        ["compose", "-f", Catalog.ComposeFile, "--profile", service, "stop", service], timeoutMs: 120_000);
}

public sealed class WslSwitch() : Switchable("WSL 2",
    "Máquina virtual de Linux (Ubuntu, Debian, Fedora) y de Docker Desktop.")
{
    private static readonly string[] Processes = ["vmmemWSL", "vmmem"];

    public override bool CanStart => false;
    public override string StopWarning =>
        "Esto apaga todas las distribuciones de WSL y también Docker Desktop con sus contenedores. ¿Continuar?";

    public override void Update(Snapshot snap)
    {
        IsRunning = snap.WslRunning.Length > 0 || snap.HasProcess(Processes);
        MemoryBytes = snap.MemoryOf(Processes);
        StatusText = snap.WslRunning.Length > 0
            ? "Encendido · " + string.Join(", ", snap.WslRunning)
            : IsRunning ? "Encendido" : "Apagado · se enciende solo al abrir una distribución";
    }

    public override Task StartAsync() => Task.CompletedTask;

    public override Task StopAsync() => Proc.RunOrThrowAsync("wsl.exe", ["--shutdown"], timeoutMs: 120_000);
}

public sealed class EmulatorSwitch() : Switchable("Emulador de Android",
    "Dispositivo virtual de Android Studio (el primer AVD que encuentre).")
{
    private static readonly string[] Processes = ["qemu-system-x86_64", "qemu-system-x86_64-headless", "emulator"];
    private static string Sdk => Environment.GetEnvironmentVariable("ANDROID_HOME") is { Length: > 0 } h ? h
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");
    private static string EmulatorExe => Path.Combine(Sdk, "emulator", "emulator.exe");
    private static string AdbExe => Path.Combine(Sdk, "platform-tools", "adb.exe");

    public override void Update(Snapshot snap)
    {
        IsAvailable = File.Exists(EmulatorExe);
        IsRunning = snap.HasProcess(Processes);
        MemoryBytes = snap.MemoryOf(Processes);
        StatusText = !IsAvailable ? "No encontré el SDK de Android" : IsRunning ? "Encendido" : "Apagado";
    }

    public override async Task StartAsync()
    {
        var list = await Proc.RunAsync(EmulatorExe, ["-list-avds"], timeoutMs: 20_000);
        var avd = list.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => !l.StartsWith("INFO", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("No hay ningún dispositivo virtual (AVD) creado.");
        Process.Start(new ProcessStartInfo(EmulatorExe) { ArgumentList = { "-avd", avd }, UseShellExecute = false, CreateNoWindow = true })?.Dispose();
    }

    public override async Task StopAsync()
    {
        await Proc.RunAsync(AdbExe, ["emu", "kill"], timeoutMs: 20_000);
        for (var i = 0; i < 20 && Process.GetProcessesByName("qemu-system-x86_64").Length > 0; i++)
            await Task.Delay(500);
        foreach (var n in Processes)
            foreach (var p in Process.GetProcessesByName(n))
                using (p) { try { p.Kill(entireProcessTree: true); } catch { /* already exiting */ } }
    }
}
