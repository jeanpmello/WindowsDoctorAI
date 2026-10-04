# Alpha de diagnóstico — Windows 11 x64

Esta Alpha é distribuída como um artefato ZIP **unpackaged** do workflow manual `Alpha package (Windows 11 x64)`. O artefato contém a pasta publicada do app e este guia (`README-ALPHA.md`). Não é MSIX nem instalador: não requer Visual Studio, .NET SDK, certificado de desenvolvedor ou identidade de editor no computador de destino. O workflow não cria Release do GitHub nem assina o executável.

## Baixar e iniciar

1. No repositório, abra **Actions** e selecione **Alpha package (Windows 11 x64)**.
2. Escolha **Run workflow** na branch preparada para a Alpha e aguarde build, testes e verificações do pacote concluírem com sucesso.
3. Baixe o artefato `WindowsDoctorAI-alpha-win11-x64` da execução concluída; o download do artefato é um ZIP.
4. Extraia-o para uma pasta local e execute `WindowsDoctorAI.App.exe`.

Use esta build somente em **Windows 11 x64**. Windows 10, Windows Server (incluindo Server 2019) e Windows em ARM não são alvos afirmados ou validados por esta Alpha. O workflow não assina o executável em nome do projeto nem declara identidade de editor ou confiança de certificado; o Windows pode identificá-lo como aplicativo de publicador desconhecido. Não existe etapa para instalar ou confiar em certificado.

## Dependências e limites

O publish é self-contained para o runtime .NET e para o Windows App SDK 1.7: esses runtimes seguem junto com o app. Conforme o [guia Microsoft de distribuição unpackaged](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app), apps unpackaged também exigem o **Microsoft Visual C++ Redistributable x64**. Se o Windows indicar que falta uma DLL do runtime VC++, instale o pacote x64 oficial mais recente pela [página Microsoft do VC++ Redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist); essa dependência não é incluída nem instalada silenciosamente pelo artefato.

Esta é uma Alpha, não uma declaração de compatibilidade de produção. Os scanners disponíveis são somente de leitura: **não há reparo automático nem alteração do Windows**. Diagnósticos e consultas de hardware/nativas precisam de validação em hosts Windows 11 reais. O comportamento do Windows Server Backup runtime não foi validado em Windows Server 2019 e não é prometido nesta build.

A execução visual da UI, permissões e respostas das APIs do Windows podem variar conforme edição, atualização, hardware e configuração do host. Os testes automatizados e a checagem do pacote no workflow não substituem um smoke test no computador de destino. O projeto ainda usa .NET 9; a [política Microsoft de suporte](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support) indica suporte até novembro de 2026, portanto essa base precisa de acompanhamento para além desse período.

O app é local. A IA não tem provedor ativo, e o inventário não é enviado a um serviço externo. O histórico local de diagnósticos é opt-in e desativado por padrão; dados necessários ao funcionamento podem ser gravados localmente, e relatórios HTML só são salvos se solicitados pela pessoa usuária.

## Enviar feedback

Abra uma [issue no WindowsDoctorAI](https://github.com/jeanpmello/WindowsDoctorAI/issues) e informe a versão/build do Windows 11, o que tentou fazer, o resultado esperado e o que ocorreu. Inclua uma mensagem de erro ou trecho de log apenas depois de remover nomes de usuário, caminhos pessoais, identificadores do computador, números de série e outros dados privados. Não envie logs completos, dumps, tokens, senhas ou dados de backup.

## Base de distribuição

O formato de pasta ZIP segue a orientação Microsoft para app WinUI 3 unpackaged: evita exigir instalação do Windows App SDK Runtime em separado ao usar deployment self-contained. O runtime .NET também é incluído no modo self-contained. Consulte o [guia Windows App SDK self-contained](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps), a [documentação de deployment do .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/) e a [documentação Microsoft para apps unpackaged](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps).
