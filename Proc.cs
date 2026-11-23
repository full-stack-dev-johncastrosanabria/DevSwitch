using System.Diagnostics;
using System.Text;

namespace DevSwitch;

/// <summary>Runs a command-line tool without a console window and captures its output.</summary>
public static class Proc
{
    public sealed record Result(int ExitCode, string Out, string Err)
    {
        public bool Ok => ExitCode == 0;

        /// <summary>Last non-empty line of stderr (or stdout), for short error messages.</summary>
        public string LastError =>
            (Err + "\n" + Out).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault() ?? $"código de salida {ExitCode}";
    }

    public static async Task<Result> RunAsync(string file, IEnumerable<string> args, int timeoutMs = 60_000, Encoding? encoding = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (encoding != null)
        {
            psi.StandardOutputEncoding = encoding;
            psi.StandardErrorEncoding = encoding;
        }

        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (Exception ex) { return new Result(-1, "", $"No se pudo ejecutar {file}: {ex.Message}"); }

        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return new Result(-2, "", $"{Path.GetFileName(file)} no respondió a tiempo");
        }
        return new Result(p.ExitCode, await stdout, await stderr);
    }

    public static async Task RunOrThrowAsync(string file, IEnumerable<string> args, int timeoutMs = 60_000)
    {
        var r = await RunAsync(file, args, timeoutMs);
        if (!r.Ok) throw new InvalidOperationException(r.LastError);
    }
}
