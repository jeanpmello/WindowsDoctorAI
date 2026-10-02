# Windows Doctor AI

Aplicativo desktop Windows em WinUI 3/.NET 9 para inventário local e diagnóstico assistido. Os Milestones 1 e 2 têm código implementado; execução visual e consultas dependentes do Windows ainda precisam de validação em uma máquina Windows.

## O que está incluído

- Solution em Clean Architecture com projetos independentes em `src/` e testes em `tests/`.
- App WinUI 3 com telas Inicial, Sobre e Configurações, MVVM Toolkit, Dependency Injection, logging, configuração e SQLite local opcional.
- Scanner de inventário do computador e cinco plugins somente de leitura: Windows Update, Services, Drivers, Disk e Event Viewer.
- Modelo comum de resultado, execução paralela quando segura, medição por scanner, isolamento de falhas e consolidação para dashboard e relatório textual.
- Dashboard com Health Score calculado quando existem verificações confirmadas, problemas críticos, avisos, duração, última execução, cobertura por categoria e evidências.
- Inventário do Milestone 1 preservado no dashboard e no histórico. Registros antigos sem resultados do engine continuam carregáveis, mas não recebem score diagnóstico calculado.
- IA sem provedor ativo, e nenhum executor de reparo; não instala atualizações/drivers, altera serviços/Registro nem remove arquivos.

O score desta etapa é heurístico: 100 menos 25 por achado crítico e 8 por aviso, limitado a 0–100. Se nenhuma verificação for confirmada, o score é “Não calculado”. Dados indisponíveis ou não verificados não contam como estado saudável. As consultas dependem de APIs e permissões Windows; SMART e temperatura variam por hardware/controlador. Rede e Segurança ainda não têm scanners no Milestone 2 e aparecem como não verificadas.

## Estrutura

```text
WindowsDoctorAI.sln
├── src/
│   ├── WindowsDoctorAI.App/            # WinUI 3, MVVM, composição e dashboard
│   ├── WindowsDoctorAI.Core/           # IDiagnosticScanner, IDiagnosticEngine e portas
│   ├── WindowsDoctorAI.Domain/         # Inventário, resultados, relatório e execução
│   ├── WindowsDoctorAI.Application/    # Diagnostic Engine e caso de uso
│   ├── WindowsDoctorAI.Diagnostics/    # Plugins e adaptadores locais Windows
│   ├── WindowsDoctorAI.Reporting/      # Formatadores de relatório textual
│   ├── WindowsDoctorAI.Infrastructure/# Registro de adaptadores
│   ├── WindowsDoctorAI.Database/      # EF Core, SQLite e repositórios
│   ├── WindowsDoctorAI.Repair/         # Catálogo sem executor de reparos
│   └── WindowsDoctorAI.AI/             # Contrato opcional; sem provedor
└── tests/WindowsDoctorAI.Tests/        # Testes unitários e integração SQLite
```

Consulte [PROJECT_RULES.md](PROJECT_RULES.md), [CONTRIBUTING.md](CONTRIBUTING.md), [docs/Architecture.md](docs/Architecture.md) e [docs/Roadmap.md](docs/Roadmap.md).

## Pré-requisitos

- Windows 10 (build mínimo de plataforma `10.0.17763.0`) ou Windows 11 para executar a UI e consultar Windows Update, Registro, WMI e Event Viewer.
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) e conexão à internet na primeira restauração dos pacotes NuGet.
- Visual Studio 2022 atualizado com ferramentas para desenvolvimento desktop .NET recomendadas para WinUI 3 (alternativamente, SDK .NET e Windows App SDK restaurados via NuGet).

A solução foi solicitada em .NET 9. A política da Microsoft informa fim de suporte do .NET 9 em 10 de novembro de 2026; o alvo deverá ser reavaliado antes de uma versão de produção.

## Compilar e executar no Windows

Na raiz do repositório, em PowerShell ou Developer Command Prompt:

```powershell
dotnet restore .\WindowsDoctorAI.sln
dotnet build .\WindowsDoctorAI.sln --configuration Release
dotnet test .\tests\WindowsDoctorAI.Tests\WindowsDoctorAI.Tests.csproj --configuration Release
dotnet run --project .\src\WindowsDoctorAI.App\WindowsDoctorAI.App.csproj --configuration Release
```

Ao abrir, selecione **Iniciar Diagnóstico** na tela Inicial. O app cria o banco `windowsdoctorai.db` em `%LOCALAPPDATA%\WindowsDoctorAI\`. O schema inicial continua sendo criado com `EnsureCreated`; migrações versionadas ainda não existem.

## Testes e limites

A suíte usa fontes falsas em memória e SQLite para verificar score, paralelismo, falhas isoladas, normalização, filtros dos cinco plugins, estados indisponíveis, formatação e persistência. Ela não prova que Windows Update, WMI, SMART ou Event Viewer foram exercitados em hardware real.

No Linux, é possível executar os testes .NET e compilar as bibliotecas multiplataforma. A validação de XAML/WinUI depende do compilador XAML do Windows (`XamlCompiler.exe`) e deve ser feita em Windows; este ambiente não valida a execução da interface nem das APIs nativas.

## Persistência e privacidade

O histórico é ligado por padrão e grava localmente o inventário e o relatório, que podem conter nome/série do equipamento, usuário/IP, identificadores Plug and Play e mensagens dos eventos locais. Desative **Salvar diagnósticos no histórico local** para evitar novas gravações. Essa opção não apaga dados já salvos; remoção de histórico não faz parte deste milestone. SQLite não implica criptografia em repouso. Consulte [docs/Database.md](docs/Database.md) e [docs/Security.md](docs/Security.md).

## Licença

[MIT](LICENSE).
