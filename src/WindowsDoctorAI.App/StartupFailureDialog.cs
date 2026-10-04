using System.Runtime.InteropServices;

namespace WindowsDoctorAI.App;

/// <summary>Mostra um aviso de inicialização sem expor mensagens, caminhos ou dados da exceção.</summary>
internal static class StartupFailureDialog
{
    private const uint ErrorIcon = 0x00000010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr windowHandle, string text, string caption, uint type);

    public static void Show(int hresult)
    {
        var message = string.Format(
            "O Windows Doctor AI não conseguiu concluir a inicialização.\r\n\r\nCódigo técnico: 0x{0:X8}\r\n\r\nNenhuma varredura de diagnóstico foi iniciada. Consulte README-ALPHA.md e informe apenas esse código ao relatar o problema. Não envie dumps ou dados pessoais.",
            unchecked((uint)hresult));

        _ = MessageBoxW(IntPtr.Zero, message, "Windows Doctor AI", ErrorIcon);
    }
}
