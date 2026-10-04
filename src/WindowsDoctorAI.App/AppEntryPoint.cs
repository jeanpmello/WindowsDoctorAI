using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WindowsDoctorAI.App;

/// <summary>Ponto de entrada unpackaged: inicializa COM/WinRT e o contexto do Dispatcher UI.</summary>
internal static class AppEntryPoint
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            global::Microsoft.UI.Xaml.Application.Start(startupArgs =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }
        catch (Exception exception)
        {
            StartupFailureDialog.Show(exception.HResult);
            Environment.ExitCode = 1;
        }
    }
}
