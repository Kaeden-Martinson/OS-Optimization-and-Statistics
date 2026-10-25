using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using PCSpecOverlay.Core;

namespace PCSpecOverlay;

public sealed record StartupStatus(bool Enabled, string? Command, bool PointsHere)
{
    public string Describe() =>
        !Enabled ? "Off"
        : PointsHere ? "On"
        : Command is null ? "On (target unknown)"
        : File.Exists(Command) ? $"On, but starts another copy: {Installer.Describe(Command)}"
        : $"On, but the app it starts no longer exists ({Installer.Describe(Command)})";
}

/// <summary>
/// "Start with Windows" as a Task Scheduler logon task for the current user.
/// <list type="bullet">
/// <item>Standard accounts: a normal (least-privilege) task, created without any prompt. FPS then works if
/// the account is in "Performance Log Users".</item>
/// <item>Admin accounts: a "highest available" task so FPS works without a UAC prompt at every logon;
/// registering it needs one UAC approval, which Windows shows.</item>
/// </list>
/// It runs on battery, has no time limit, and starts 3 s after logon. Exactly one task (\PCSpecOverlay) is ever created.
/// </summary>
internal static class StartupTask
{
    private static string TaskName => "PCSpecOverlay" + AppIdentity.Suffix;

    public static bool IsElevated =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>True for admin accounts, whether or not this process is currently elevated.</summary>
    public static bool IsAdminAccount
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            // An unelevated admin token still carries Administrators as a deny-only SID
            return IsElevated || identity.Claims.Any(c => c.Value == "S-1-5-32-544");
        }
    }

    public static StartupStatus Status()
    {
        var (code, output) = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");
        if (code != 0) return new StartupStatus(false, null, false);
        var command = ParseCommand(output);
        if (command is null)
            Log.Warn("Startup", $"Couldn't read the startup task's target ({output.Length} chars: {AppIdentity.Redact(output[..Math.Min(80, output.Length)]).Replace("\0", "\\0")})");
        var here = command != null && string.Equals(Path.GetFullPath(command), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        return new StartupStatus(true, command, here);
    }

    /// <summary>The program a task XML runs, unescaped; null if there isn't one.</summary>
    internal static string? ParseCommand(string taskXml)
    {
        var match = Regex.Match(taskXml, "<Command>(.*?)</Command>", RegexOptions.Singleline);
        if (!match.Success) return null;
        var text = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim().Trim('"');
        return text.Length == 0 ? null : text;
    }

    /// <returns>Null on success, otherwise what went wrong in plain words.</returns>
    public static string? SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            var (code, _) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            if (code == 0 || !Status().Enabled) { Log.Info("Startup", "Start with Windows turned off"); return null; }
            if (IsAdminAccount && RunElevated($"/Delete /TN \"{TaskName}\" /F") == 0) return null;
            return "Couldn't remove the startup task.";
        }

        var xmlPath = Path.Combine(Path.GetTempPath(), $"PCSpecOverlay-task-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(xmlPath, TaskXml(), Encoding.Unicode);
            var args = $"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F";
            int code;
            if (IsAdminAccount && !IsElevated)
            {
                // Registering a highest-privilege task needs one approval; Windows shows the UAC prompt
                code = RunElevated(args);
                if (code != 0) return "Windows didn't approve creating the startup task (the UAC prompt was cancelled).";
            }
            else
            {
                (code, var output) = RunSchtasks(args);
                if (code != 0) return $"Couldn't create the startup task: {output.Trim()}";
            }
            Log.Info("Startup", $"Start with Windows on ({Installer.Describe(Environment.ProcessPath)})");
            return Status().PointsHere ? null : "The startup task was created but doesn't point at this app.";
        }
        catch (Exception e)
        {
            Log.Error("Startup", "Couldn't create the startup task", e);
            return $"Couldn't create the startup task: {e.Message}";
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* temp file */ }
        }
    }

    /// <summary>
    /// If the installed app is running but the task still points somewhere else (it was moved or
    /// reinstalled), re-point the task. Never prompts: admin accounts are only repaired when elevated.
    /// </summary>
    public static void RepairIfMoved()
    {
        if (!Installer.IsInstalledCopy) return;
        var status = Status();
        if (!status.Enabled || status.PointsHere) return;
        if (IsAdminAccount && !IsElevated)
        {
            Log.Warn("Startup", $"The startup task points at {Installer.Describe(status.Command)}; turn Start with Windows off and on to fix it");
            return;
        }
        Log.Info("Startup", $"Re-pointing the startup task from {Installer.Describe(status.Command)} to this copy");
        SetEnabled(true);
    }

    private static string TaskXml()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = SecurityElement.Escape(Environment.ProcessPath!);
        var dir = SecurityElement.Escape(AppContext.BaseDirectory);
        // Standard accounts get "Access is denied" registering a HighestAvailable task
        var runLevel = IsAdminAccount ? "HighestAvailable" : "LeastPrivilege";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{AppIdentity.ProductName} - performance overlay</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT3S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>{runLevel}</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{exe}</Command><WorkingDirectory>{dir}</WorkingDirectory></Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int Code, string Output) RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = new MemoryStream();
            var errTask = p.StandardError.ReadToEndAsync();
            p.StandardOutput.BaseStream.CopyTo(stdout);
            p.WaitForExit();
            return (p.ExitCode, Decode(stdout.ToArray()) + errTask.Result);
        }
        catch (Exception e)
        {
            return (-1, e.Message);
        }
    }

    /// <summary>
    /// schtasks writes UTF-16 when the caller has no console (like this windowed app) and the console
    /// code page otherwise, so pick by looking at the bytes.
    /// </summary>
    internal static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        var zeros = bytes.Where((b, i) => i % 2 == 1 && b == 0).Count();
        return bytes.Length > 8 && zeros > bytes.Length / 4 ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
    }

    private static int RunElevated(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            })!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch
        {
            return -1; // UAC declined
        }
    }
}
