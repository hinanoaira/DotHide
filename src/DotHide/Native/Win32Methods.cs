using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DotHide.Native;

/// <summary>
/// Direct Win32 API bindings for file attributes and console operations.
/// Avoids the overhead of starting external processes like cmd.exe or attrib.exe.
/// </summary>
public static class Win32Methods
{
    public const uint FILE_ATTRIBUTE_READONLY = 0x00000001;
    public const uint FILE_ATTRIBUTE_HIDDEN = 0x00000002;
    public const uint FILE_ATTRIBUTE_SYSTEM = 0x00000004;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    public const uint FILE_ATTRIBUTE_ARCHIVE = 0x00000020;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    public const uint FILE_ATTRIBUTE_TEMPORARY = 0x00000100;
    public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;

    public const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

    // Win32 Error Codes
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_SHARING_VIOLATION = 32;
    public const int ERROR_LOCK_VIOLATION = 33;

    // Console Attach and StdHandle constants
    public const int ATTACH_PARENT_PROCESS = -1;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", EntryPoint = "GetFileAttributesW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetFileAttributesW(string lpFileName);

    [DllImport("kernel32.dll", EntryPoint = "SetFileAttributesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetFileAttributesW(string lpFileName, uint dwFileAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeConsole();

    /// <summary>
    /// Gets formatted Win32 error description.
    /// </summary>
    public static string GetErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            ERROR_SUCCESS => "The operation completed successfully.",
            ERROR_FILE_NOT_FOUND => "File not found (ERROR_FILE_NOT_FOUND, code 2).",
            ERROR_PATH_NOT_FOUND => "Path not found (ERROR_PATH_NOT_FOUND, code 3).",
            ERROR_ACCESS_DENIED => "Access is denied (ERROR_ACCESS_DENIED, code 5).",
            ERROR_SHARING_VIOLATION => "Sharing violation - file locked by another process (ERROR_SHARING_VIOLATION, code 32).",
            ERROR_LOCK_VIOLATION => "Lock violation (ERROR_LOCK_VIOLATION, code 33).",
            _ => $"Win32 error code {errorCode}."
        };
    }
}
