using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace PCSpecOverlay;

/// <summary>
/// "Start with Windows" via a Task Scheduler logon task with highest privileges. For admin accounts
/// the overlay starts elevated (enabling FPS) without a UAC prompt; standard accounts get a normal
/// logon task and need the "Performance Log Users" group for FPS.
/// </summary>
internal static class StartupTask
{
    private const string TaskName = "PCSpecOverlay";

    public static bool IsElevated =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>True for admin accounts, whether or not this process is currently elevated.</summary>
    private static bool IsAdminAccount
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            // An unelevated admin token still carries Administrators as a deny-only SID
            return IsElevated || identity.Claims.Any(c => c.Value == "S-1-5-32-544");
        }
    }

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"", elevate: false) == 0;

    public static bool SetEnabled(bool enabled)
    {
        if (!enabled)
            return RunSchtasks($"/Delete /TN \"{TaskName}\" /F", elevate: false) == 0
                || (IsAdminAccount && RunSchtasks($"/Delete /TN \"{TaskName}\" /F", elevate: true) == 0);

        var xmlPath = Path.Combine(Path.GetTempPath(), "PCSpecOverlay-task.xml");
        File.WriteAllText(xmlPath, TaskXml(), Encoding.Unicode);
        try
        {
            // Standard accounts can register their own logon task; admin accounts need elevation
            // to register one that runs with highest privileges.
            var args = $"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F";
            return IsAdminAccount && !IsElevated
                ? RunSchtasks(args, elevate: true) == 0
                : RunSchtasks(args, elevate: false) == 0;
        }
        finally
        {
            File.Delete(xmlPath);
        }
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
              <RegistrationInfo><Description>PC Spec Overlay - performance overlay</Description></RegistrationInfo>
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

    private static int RunSchtasks(string args, bool elevate)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args) { WindowStyle = ProcessWindowStyle.Hidden };
        if (elevate)
        {
            psi.UseShellExecute = true;
            psi.Verb = "runas";
        }
        else
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        }
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch
        {
            return -1; // UAC declined
        }
    }
}
