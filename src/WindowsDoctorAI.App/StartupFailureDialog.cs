using System.Runtime.InteropServices;

namespace WindowsDoctorAI.App;

/// <summary>Mostra um aviso de inicialização sem expor mensagens, caminhos ou dados da exceção.</summary>
internal static class StartupFailureDialog
{
    private const uint ErrorIcon = 0x00000010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr windowHandle, string text, string caption, uint type);

    public static void Show(StartupFailureDetails details)
    {
        _ = MessageBoxW(IntPtr.Zero, details.ToDisplayText(), "Windows Doctor AI", ErrorIcon);
    }
}
