using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CodexProxyLauncher;

internal sealed record LaunchResult(int ExitCode, string Output, string Error)
{
    public string FailureDetail => !string.IsNullOrWhiteSpace(Error) ? Error.Trim()
        : !string.IsNullOrWhiteSpace(Output) ? Output.Trim()
        : $"PowerShell exit code: {ExitCode}";
}

internal static class PowerShellLauncher
{
    // These exit codes are returned by Launcher.ps1 for handled states.
    public const int CodexRunningExitCode = 3;
    public const int LaunchInProgressExitCode = 4;
    private const string PayloadEnvironmentVariable = "CODEX_PROXY_LAUNCHER_PAYLOAD";

    private static byte[] ReadPayload()
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.ps1")
            ?? throw new InvalidOperationException("Embedded script is missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static string PayloadHash
    {
        get
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(ReadPayload())).Replace("-", "");
        }
    }

    private static string AsData(string value) =>
        "[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" +
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "'))";

    private static string BuildCommand(LauncherOptions options)
    {
        var command = new StringBuilder();
        command.AppendLine("$ErrorActionPreference = 'Stop'");
        command.AppendLine("[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)");
        command.AppendLine("try {");
        command.AppendLine($"  $payload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:{PayloadEnvironmentVariable}))");
        command.AppendLine($"  $env:{PayloadEnvironmentVariable} = $null");
        command.AppendLine("  $proxy = " + AsData(options.Proxy));
        command.AppendLine("  $appId = " + AsData(options.AppId));
        if (options.SelfTest)
        {
            command.AppendLine("  $tokens = $null; $parseErrors = $null");
            command.AppendLine("  $ast = [System.Management.Automation.Language.Parser]::ParseInput($payload, [ref]$tokens, [ref]$parseErrors)");
            command.AppendLine("  if ($parseErrors.Count) { throw (($parseErrors | ForEach-Object { $_.Message }) -join [Environment]::NewLine) }");
            command.AppendLine("  $parameters = @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })");
            command.AppendLine("  if ($parameters -notcontains 'Proxy' -or $parameters -notcontains 'AppId') { throw 'Expected script parameters are missing.' }");
            command.AppendLine("  if (-not ($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'RestartRunning' -and $_.StaticType -eq [switch] })) { throw 'Expected restart switch is missing.' }");
            command.AppendLine("  [Console]::WriteLine('PASS: embedded PowerShell script parsed.')");
            command.AppendLine("  [Console]::WriteLine('Proxy: ' + $proxy)");
            command.AppendLine("  [Console]::WriteLine('AppId: ' + $appId)");
            command.AppendLine($"  [Console]::WriteLine('Restart running: {(options.RestartRunning ? "enabled" : "disabled")}')");
            command.AppendLine("  [Console]::WriteLine('No application was activated.')");
            command.AppendLine("  exit 0");
        }
        else
        {
            command.AppendLine($"  $status = & ([scriptblock]::Create($payload)) -Proxy $proxy -AppId $appId -RestartRunning:${(options.RestartRunning ? "true" : "false")}");
            command.AppendLine("  exit $status");
        }
        command.AppendLine("} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }");
        return command.ToString();
    }

    public static LaunchResult Execute(LauncherOptions options)
    {
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Windows PowerShell was not found.", executable);
        string arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildCommand(options)));
        if (arguments.Length > 30000)
            throw new ArgumentException("Command line is too long.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };
        process.StartInfo.EnvironmentVariables[PayloadEnvironmentVariable] = Convert.ToBase64String(ReadPayload());
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            if (!process.HasExited)
                process.Kill();
            process.WaitForExit();
            throw new TimeoutException("PowerShell did not finish within 60 seconds.");
        }
        return new LaunchResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
