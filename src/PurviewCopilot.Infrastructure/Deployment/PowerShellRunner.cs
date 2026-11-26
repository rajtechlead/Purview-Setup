using System.Diagnostics;
using System.Text;
using PurviewCopilot.Application.Abstractions;

namespace PurviewCopilot.Infrastructure.Deployment;

/// <summary>
/// Runs a script through Windows PowerShell with a fixed, explicit environment. No shell
/// interpolation is used: the script is piped on stdin and arguments are never concatenated.
/// </summary>
public sealed class PowerShellRunner : IPowerShellRunner
{
    private readonly TimeSpan _timeout;

    public PowerShellRunner(TimeSpan? timeout = null) =>
        _timeout = timeout ?? TimeSpan.FromMinutes(10);

    public async Task<PowerShellResult> RunAsync(
        string script,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("-");

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.StandardInput.WriteAsync(script.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new PowerShellResult(-1, await outputTask.ConfigureAwait(false), $"Timed out after {_timeout.TotalMinutes:0} minutes.");
        }

        return new PowerShellResult(
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }
}
