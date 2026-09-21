using System;
using System.Runtime.InteropServices;

namespace SPTushonka.Custom.Utils;

/// <summary>
/// Native Windows message box, for errors that must reach the player before the game UI exists.
/// </summary>
public static class MessageBoxHelper
{
    public enum MessageBoxType : uint
    {
        ABORTRETRYIGNORE = 0x00000002 | 0x00000010,
        CANCELTRYCONTINUE = 0x00000006 | 0x00000030,
        HELP = 0x00004000 | 0x00000040,
        OK = 0x00000000 | 0x00000040,
        OKCANCEL = 0x00000001 | 0x00000040,
        RETRYCANCEL = 0x00000005,
        YESNO = 0x00000004 | 0x00000040,
        YESNOCANCEL = 0x00000003 | 0x00000040,
        DEFAULT = 0x00000000 | 0x00000010,
    }

    public enum MessageBoxResult
    {
        ERROR = -1,
        OK = 1,
        CANCEL = 2,
        ABORT = 3,
        RETRY = 4,
        IGNORE = 5,
        YES = 6,
        NO = 7,
        TRY_AGAIN = 10,
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);

    public static IntPtr GetWindowHandle()
    {
        return GetActiveWindow();
    }

    public static MessageBoxResult Show(string text, string caption, MessageBoxType type = MessageBoxType.DEFAULT)
    {
        try
        {
            return (MessageBoxResult)MessageBox(GetWindowHandle(), text, caption, (uint)type);
        }
        catch (Exception)
        {
            // No user32 under some Wine setups, the caller still logs the error
            return MessageBoxResult.ERROR;
        }
    }
}
