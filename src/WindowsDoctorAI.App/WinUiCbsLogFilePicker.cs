using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WindowsDoctorAI.Core;
using WinRT.Interop;

namespace WindowsDoctorAI.App;

/// <summary>Seletor WinUI de CBS.log; retorna apenas stream somente leitura, sem propagar nome ou caminho.</summary>
public sealed class WinUiCbsLogFilePicker(Func<nint> ownerHandleProvider) : ICbsLogFilePicker
{
    public async Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".log");
        InitializeWithWindow.Initialize(picker, ownerHandleProvider());

        var file = await picker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (file is null) return null;

        return new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
    }
}
