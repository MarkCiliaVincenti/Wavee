﻿using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Wavee.UI.Helpers;

public static class WindowsStartupHelper
{
    private const string KeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";

    public static void AddOrRemoveRegistryKey(bool runOnSystemStartup)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new InvalidOperationException("Registry modification can only be done on Windows.");
        }

        string pathToExeFile = EnvironmentHelpers.GetExecutablePath();

        string pathToExecWithArgs = $"{pathToExeFile} {StartupHelper.SilentArgument}";

        if (!File.Exists(pathToExeFile))
        {
            throw new InvalidOperationException($"Path: {pathToExeFile} does not exist.");
        }

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true) ?? throw new InvalidOperationException("Registry operation failed.");

        var existingPath = key.GetValue(nameof(WalletWasabi));
        if (existingPath is null && runOnSystemStartup)
        {
            key.SetValue(nameof(WalletWasabi), pathToExecWithArgs);
        }
        else if (existingPath is not null && !runOnSystemStartup)
        {
            key.DeleteValue(nameof(WalletWasabi), false);
        }
    }
}