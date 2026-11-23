using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text;

namespace DevSwitch;

public sealed record ServiceInfo(string Name, string State, string StartMode, int ProcessId);

/// <summary>
/// One read of everything the switches need (processes, services, Docker, WSL), taken off the UI thread
/// so that each refresh calls the slow tools only once.
/// </summary>
public sealed class Snapshot
{
    public Dictionary<int, long> WorkingSetByPid { get; } = new();
    public Dictionary<string, List<int>> PidsByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ServiceInfo> Services { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool DockerUp { get; private set; }
    /// <summary>Running Compose services of the devdb project, keyed by service name, with memory in bytes.</summary>
    public Dictionary<string, long> ComposeMemory { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] WslRunning { get; private set; } = [];

    public bool HasProcess(params string[] names) => names.Any(n => PidsByName.ContainsKey(n));

    public long MemoryOf(params string[] processNames) =>
        processNames.SelectMany(n => PidsByName.TryGetValue(n, out var l) ? l : [])
            .Distinct()
            .Sum(pid => WorkingSetByPid.GetValueOrDefault(pid));

    public static async Task<Snapshot> TakeAsync()
    {
        var s = new Snapshot();
        s.ReadProcesses();
        s.ReadServices();
        await Task.WhenAll(s.ReadDockerAsync(), s.ReadWslAsync());
        return s;
    }

    private void ReadProcesses()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    WorkingSetByPid[p.Id] = p.WorkingSet64;
                    if (!PidsByName.TryGetValue(p.ProcessName, out var list))
                        PidsByName[p.ProcessName] = list = [];
                    list.Add(p.Id);
                }
                catch (InvalidOperationException) { /* exited while listing */ }
            }
        }
    }

    private void ReadServices()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name, State, StartMode, ProcessId FROM Win32_Service");
        using var results = searcher.Get();
        foreach (ManagementObject mo in results)
        {
            using (mo)
            {
                var name = (string)mo["Name"];
                Services[name] = new ServiceInfo(name, (string)mo["State"], (string)mo["StartMode"], Convert.ToInt32(mo["ProcessId"]));
            }
        }
    }

    private async Task ReadDockerAsync()
    {
        // Skip the docker CLI entirely when Docker Desktop's backend isn't running: it would just time out.
        if (!HasProcess("com.docker.backend")) return;

        var info = await Proc.RunAsync("docker", ["info", "--format", "{{.ServerVersion}}"], timeoutMs: 8_000);
        DockerUp = info.Ok && !string.IsNullOrWhiteSpace(info.Out);
        if (!DockerUp) return;

        var ps = await Proc.RunAsync("docker",
            ["ps", "--filter", $"label=com.docker.compose.project={Catalog.ComposeProject}",
             "--format", "{{.Names}}|{{.Label \"com.docker.compose.service\"}}"], timeoutMs: 10_000);
        if (!ps.Ok) return;

        var serviceByContainer = ps.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('|'))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);
        if (serviceByContainer.Count == 0) return;

        foreach (var svc in serviceByContainer.Values) ComposeMemory[svc] = 0;

        var stats = await Proc.RunAsync("docker",
            ["stats", "--no-stream", "--format", "{{.Name}}|{{.MemUsage}}", .. serviceByContainer.Keys], timeoutMs: 15_000);
        if (!stats.Ok) return;
        foreach (var line in stats.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length == 2 && serviceByContainer.TryGetValue(parts[0], out var svc))
                ComposeMemory[svc] = ParseDockerSize(parts[1].Split('/')[0].Trim());
        }
    }

    private async Task ReadWslAsync()
    {
        // wsl.exe writes UTF-16 when its output is redirected.
        var r = await Proc.RunAsync("wsl.exe", ["--list", "--running", "--quiet"], timeoutMs: 10_000, encoding: Encoding.Unicode);
        if (!r.Ok) return;
        WslRunning = r.Out.Replace("\0", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Parses docker's "512.3MiB" / "1.2GiB" / "800kB" into bytes.</summary>
    internal static long ParseDockerSize(string text)
    {
        var i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
        if (!double.TryParse(text[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return 0;
        var unit = text[i..].Trim().ToUpperInvariant();
        double factor = unit switch
        {
            "KIB" => 1024, "KB" => 1000,
            "MIB" => 1024d * 1024, "MB" => 1e6,
            "GIB" => 1024d * 1024 * 1024, "GB" => 1e9,
            _ => 1,
        };
        return (long)(value * factor);
    }
}
