using System.Diagnostics;
using System.IO;
using System.Text;

namespace DnsSelector.Services;

public sealed class PowerShellException : Exception
{
    public PowerShellException(string message) : base(message) { }
}

public sealed class PowerShellRunner : IPowerShellRunner
{
    public async Task<string> RunAsync(string script, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PowershellPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(PrepareScript(script));

        using var proc = Process.Start(psi)
            ?? throw new PowerShellException("powershell.exe を起動できませんでした。");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            var detail = stderr.Trim();
            throw new PowerShellException(
                $"PowerShell コマンドが失敗しました (終了コード {proc.ExitCode}).{(detail.Length > 0 ? "\n" + detail : "")}");
        }
        return stdout;
    }

    private static string PrepareScript(string script)
        => "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [System.Text.Encoding]::UTF8; " + script;

    private static string PowershellPath()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(path) ? path : "powershell.exe";
    }
}
