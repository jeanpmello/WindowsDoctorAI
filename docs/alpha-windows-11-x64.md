# Alpha de diagnóstico — Windows 11 x64

Esta Alpha é distribuída como um artefato ZIP **unpackaged** do workflow `Alpha package (Windows 11 x64)`. O artefato contém a pasta publicada do app e este guia (`README-ALPHA.md`). Não é MSIX nem instalador: não requer Visual Studio, .NET SDK, certificado de desenvolvedor ou identidade de editor no computador de destino. O workflow não cria Release do GitHub nem assina o executável. Build e testes também rodam automaticamente em pull requests para `main` e em pushes para `main`; nesses eventos, o ZIP Alpha não é empacotado. Para gerar o artefato, use **Run workflow**.

## Baixar e iniciar

1. No repositório, abra **Actions** e selecione **Windows CI**.
2. Escolha **Run workflow**, selecione a branch preparada para a Alpha e inicie a execução. Nessa branch, o workflow CI chama o workflow reutilizável `Alpha package (Windows 11 x64)` após o build e os testes.
3. Aguarde os jobs `Build and test (Windows)` e `Package Alpha artifact` concluírem com sucesso. Baixe `WindowsDoctorAI-alpha-win11-x64` da execução; o download do artefato é um ZIP.
4. Extraia todo o ZIP para uma pasta local e execute `Start-WindowsDoctorAI.cmd`. O launcher confere o runtime VC++ x64 antes de abrir o app e deixa uma mensagem na janela do terminal se o pré-requisito estiver ausente ou se o processo retornar erro. O executável `WindowsDoctorAI.App.exe` continua disponível, mas iniciar pelo launcher é preferível para facilitar o diagnóstico.

Use esta build somente em **Windows 11 x64**. Windows 10, Windows Server (incluindo Server 2019) e Windows em ARM não são alvos afirmados ou validados por esta Alpha. O workflow não assina o executável em nome do projeto nem declara identidade de editor ou confiança de certificado; o Windows pode identificá-lo como aplicativo de publicador desconhecido. Não existe etapa para instalar ou confiar em certificado.

## Dependências e limites

O publish é self-contained para o runtime .NET e para o Windows App SDK 1.7: esses runtimes seguem junto com o app. Conforme o [guia Microsoft de distribuição unpackaged](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app), apps unpackaged também exigem o **Microsoft Visual C++ Redistributable x64**. O launcher confere o registro de instalação do runtime x64 e, se não o detectar, para antes de abrir o app e aponta para a [página oficial da Microsoft do VC++ Redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). Ele não baixa, instala nem altera componentes; a dependência não está incluída no ZIP. Se a checagem indicar ausência, siga as instruções oficiais para obter o pacote x64 mais recente e só prossiga se optar por instalá-lo.

Esta é uma Alpha, não uma declaração de compatibilidade de produção. Os scanners disponíveis são somente de leitura: **não há reparo automático nem alteração do Windows**. Diagnósticos e consultas de hardware/nativas precisam de validação em hosts Windows 11 reais. O comportamento do Windows Server Backup runtime não foi validado em Windows Server 2019 e não é prometido nesta build.

A execução visual da UI, permissões e respostas das APIs do Windows podem variar conforme edição, atualização, hardware e configuração do host. Os testes automatizados e a checagem do pacote no workflow não substituem um smoke test no computador de destino. O projeto ainda usa .NET 9; a [política Microsoft de suporte](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support) indica suporte até novembro de 2026, portanto essa base precisa de acompanhamento para além desse período.

O app é local. A IA não tem provedor ativo, e o inventário não é enviado a um serviço externo. O histórico local de diagnósticos é opt-in e desativado por padrão; dados necessários ao funcionamento podem ser gravados localmente, e relatórios HTML só são salvos se solicitados pela pessoa usuária.

## Verificar somente o XAML da janela

Para testar a inicialização XAML do pacote sem abrir a janela, iniciar inventário ou executar reparos, abra o PowerShell na pasta extraída e rode:

```powershell
$result = Join-Path $env:TEMP "WindowsDoctorAI-xaml-smoke-$([guid]::NewGuid().ToString('N')).txt"
& .\WindowsDoctorAI.App.exe --xaml-smoke-test --xaml-smoke-test-result $result
$exitCode = $LASTEXITCODE
if (Test-Path $result) { Get-Content -Path $result }
if ($exitCode -ne 0) { throw "O smoke test falhou (código $exitCode)." }
```

`PASS` confirma a construção de uma `Window`, a leitura de um `Grid` em memória com `XamlReader.Load` e a construção de AboutPage, SettingsPage, HomePage e MainWindow, incluindo o retorno de `InitializeComponent()` no thread STA real do WinUI. `FAIL` identifica o último estágio, tipo de exceção e HRESULT; nenhuma janela é ativada, e o teste não valida a interface visual nem o comportamento completo de inicialização do aplicativo. O caminho de saída deve ser um `.txt` com prefixo `WindowsDoctorAI-xaml-smoke-` diretamente na pasta temporária do usuário.

## Enviar feedback

Abra uma [issue no WindowsDoctorAI](https://github.com/jeanpmello/WindowsDoctorAI/issues) e informe a versão/build do Windows 11, o que tentou fazer, o resultado esperado e o que ocorreu. Se a caixa de inicialização for exibida, informe somente o código técnico mostrado. Não envie logs completos, dumps, tokens, senhas, identificadores do computador ou dados de backup.

## Base de distribuição

O formato de pasta ZIP segue a orientação Microsoft para app WinUI 3 unpackaged: evita exigir instalação do Windows App SDK Runtime em separado ao usar deployment self-contained. O runtime .NET também é incluído no modo self-contained. Consulte o [guia Windows App SDK self-contained](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps), a [documentação de deployment do .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/) e a [documentação Microsoft para apps unpackaged](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps).
