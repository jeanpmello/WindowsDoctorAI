using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WindowsDoctorAI.Application;
using WinRT.Interop;

namespace WindowsDoctorAI.App;

public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs args) => await _viewModel.LoadLatestAsync();

    private async void ChooseKnowledgePackage_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, GetOwnerHandle());

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > (ulong)KnowledgeJsonImporter.MaximumPackageBytes)
            {
                _viewModel.ShowPackagePreviewError($"o arquivo excede o limite de {KnowledgeJsonImporter.MaximumPackageBytes} bytes.");
                return;
            }

            var json = await FileIO.ReadTextAsync(file);
            await _viewModel.PreviewKnowledgePackageAsync(json);
        }
        catch (Exception)
        {
            _viewModel.ShowPackagePreviewError("O pacote não pôde ser lido ou validado; detalhes omitidos por privacidade.");
        }
    }

    private async void SaveHtmlReport_Click(object sender, RoutedEventArgs args)
    {
        var html = await _viewModel.CreateCurrentHtmlReportAsync();
        if (html is null) return;

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"WindowsDoctorAI-{DateTime.Now:yyyyMMdd-HHmmss}"
            };
            picker.FileTypeChoices.Add("Relatório HTML", new List<string> { ".html" });
            InitializeWithWindow.Initialize(picker, GetOwnerHandle());

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await FileIO.WriteTextAsync(file, html);
            _viewModel.ReportHtmlSaved(file.Path);
        }
        catch (Exception)
        {
            _viewModel.ReportHtmlSaveFailed("Falha ao salvar no destino escolhido.");
        }
    }

    private static nint GetOwnerHandle()
    {
        var window = (global::Microsoft.UI.Xaml.Application.Current as App)?.MainWindow
            ?? throw new InvalidOperationException("A janela principal não está disponível para abrir o seletor de arquivos.");
        return WindowNative.GetWindowHandle(window);
    }
}
