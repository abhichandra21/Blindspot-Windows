using Microsoft.Win32;

namespace Blindspot;

// Start at login through the per-user Run key. No admin rights needed.
static class LoginItem
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Blindspot";

    static string Command => $"\"{Environment.ProcessPath}\"";

    // True only if the Run entry points at this copy of the app.
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string == Command;
    }

    // Returns an error message, or null on success.
    public static string? SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return $"Failed to change start at login: {e.Message}";
        }
    }
}
