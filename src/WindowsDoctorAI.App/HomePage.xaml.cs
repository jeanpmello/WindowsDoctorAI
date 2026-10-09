using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Security.Cryptography;
using WindowsDoctorAI.AI;
using WindowsDoctorAI.Application;
using WinRT.Interop;

namespace WindowsDoctorAI.App;

public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _viewModel;

    internal HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        ApplyResponsiveLayout(ActualWidth);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs args) => await _viewModel.LoadLatestAsync();

    private void Page_SizeChanged(object sender, SizeChangedEventArgs args) => ApplyResponsiveLayout(args.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < 900;
        var narrow = width < 620;

        ContentStack.Padding = narrow
            ? new Thickness(16, 18, 16, 24)
            : new Thickness(30, 26, 30, 36);
        ContentStack.Spacing = narrow ? 16 : 20;

        DiagnosticActionsStack.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        CbsActionsStack.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        BackupActionsStack.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        AiActionsStack.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;

        SetGridColumns(MetricSummaryGrid, compact ? 1 : 3);
        SetGridColumns(CategorySummaryGrid, compact ? 1 : 2);
        SetGridColumns(KnowledgeReportGrid, compact ? 1 : 2);
        SetGridColumns(InventoryGrid, compact ? 1 : 2);

        SetRows(MetricSummaryGrid, compact ? 3 : 1);
        SetRows(CategorySummaryGrid, compact ? 2 : 1);
        SetRows(KnowledgeReportGrid, compact ? 2 : 1);
        SetRows(InventoryGrid, compact ? 6 : 3);

        SetChildPositions(MetricSummaryGrid, compact, 3, 3);
        SetChildPositions(CategorySummaryGrid, compact, 2, 2);
        SetChildPositions(KnowledgeReportGrid, compact, 2, 2);
        SetChildPositions(InventoryGrid, compact, 6, 2);
    }

    private static void SetGridColumns(Grid grid, int count)
    {
        grid.ColumnDefinitions.Clear();
        for (var index = 0; index < count; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    private static void SetRows(Grid grid, int count)
    {
        grid.RowDefinitions.Clear();
        for (var index = 0; index < count; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    private static void SetChildPositions(Grid grid, bool compact, int childCount, int wideColumnCount)
    {
        for (var index = 0; index < childCount && index < grid.Children.Count; index++)
        {
            Grid.SetColumn(grid.Children[index], compact ? 0 : index % wideColumnCount);
            Grid.SetRow(grid.Children[index], compact ? index : index / wideColumnCount);
        }
    }

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

    private async void ChooseErrorScreenshot_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            InitializeWithWindow.Initialize(picker, GetOwnerHandle());

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > (ulong)OllamaDiagnosticAiProvider.MaxScreenshotBytes)
            {
                _viewModel.SetScreenshotStatus("A captura excede o limite de 8 MiB.");
                return;
            }

            var buffer = await FileIO.ReadBufferAsync(file);
            CryptographicBuffer.CopyToByteArray(buffer, out var bytes);
            var mediaType = file.FileType.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => string.Empty
            };
            await _viewModel.AnalyzeScreenshotAsync(bytes, mediaType);
        }
        catch (Exception)
        {
            _viewModel.SetScreenshotStatus("Não foi possível ler a captura; detalhes internos foram omitidos.");
        }
    }

    private static nint GetOwnerHandle()
    {
        var window = (global::Microsoft.UI.Xaml.Application.Current as App)?.MainWindow
            ?? throw new InvalidOperationException("A janela principal não está disponível para abrir o seletor de arquivos.");
        return WindowNative.GetWindowHandle(window);
    }
}
