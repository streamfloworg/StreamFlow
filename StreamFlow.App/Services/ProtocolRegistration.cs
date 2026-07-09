using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace StreamFlow.App.Services;

/// <summary>
/// Handles registration of the streamflow:// URI protocol with Windows
/// </summary>
public static class ProtocolRegistration
{
    private const string ProtocolScheme = "streamflow";
    private const string ProtocolDescription = "StreamFlow Protocol";

    /// <summary>
    /// Registers the streamflow:// protocol handler in Windows Registry
    /// </summary>
    /// <returns>True if registration succeeded, false otherwise</returns>
    public static bool RegisterProtocol()
    {
        try
        {
            // Get the path to the executable
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                System.Diagnostics.Debug.WriteLine("Cannot register protocol: executable path not found");
                return false;
            }

            // Check if already registered
            if (IsProtocolRegistered())
            {
                System.Diagnostics.Debug.WriteLine("Protocol already registered");
                return true;
            }

            try
            {
                // Try to register under HKEY_CURRENT_USER (doesn't require admin)
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolScheme}");
                if (key == null)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to create registry key");
                    return false;
                }

                // Set protocol description
                key.SetValue("", $"URL:{ProtocolDescription}");
                key.SetValue("URL Protocol", "");

                // Set default icon
                using (var iconKey = key.CreateSubKey("DefaultIcon"))
                {
                    iconKey?.SetValue("", $"\"{exePath}\",0");
                }

                // Set command to execute
                using (var commandKey = key.CreateSubKey(@"shell\open\command"))
                {
                    // Pass the URI as a command-line argument
                    commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                }

                System.Diagnostics.Debug.WriteLine($"Successfully registered {ProtocolScheme}:// protocol");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("Access denied when registering protocol. Try running as administrator.");
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error registering protocol: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if the protocol is already registered
    /// </summary>
    /// <returns>True if registered, false otherwise</returns>
    public static bool IsProtocolRegistered()
    {
        try
        {
            // Check HKEY_CURRENT_USER first
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProtocolScheme}");
            if (key != null)
            {
                var urlProtocol = key.GetValue("URL Protocol");
                return urlProtocol != null;
            }

            // Check HKEY_CLASSES_ROOT (system-wide)
            using var systemKey = Registry.ClassesRoot.OpenSubKey(ProtocolScheme);
            if (systemKey != null)
            {
                var urlProtocol = systemKey.GetValue("URL Protocol");
                return urlProtocol != null;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking protocol registration: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Unregisters the streamflow:// protocol handler from Windows Registry
    /// </summary>
    /// <returns>True if unregistration succeeded, false otherwise</returns>
    public static bool UnregisterProtocol()
    {
        try
        {
            // Remove from HKEY_CURRENT_USER
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProtocolScheme}", false);
            
            System.Diagnostics.Debug.WriteLine($"Successfully unregistered {ProtocolScheme}:// protocol");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error unregistering protocol: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if the application is running with administrator privileges
    /// </summary>
    /// <returns>True if running as admin, false otherwise</returns>
    public static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
