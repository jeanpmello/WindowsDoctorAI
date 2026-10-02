# Windows Doctor AI

Aplicativo desktop Windows para inventário local e diagnóstico assistido. **Milestone 1 implementa a solution, o dashboard WinUI 3 e o scanner de inventário**; a execução visual e a coleta em hardware Windows ainda precisam de validação em uma máquina Windows.

## O que está incluído

- Solution .NET 9 em Clean Architecture, com projetos independentes em `src/` e testes em `tests/`.
- Aplicativo WinUI 3 com navegação lateral e telas **Inicial**, **Sobre** e **Configurações**.
- ViewModels com CommunityToolkit.Mvvm, composição por Dependency Injection, configuração JSON, logging e armazenamento local SQLite via Entity Framework Core.
- Botão **Iniciar Diagnóstico** e scanner `ComputerInventoryScanner`, que solicita inventário local de fabricante, modelo, número de série, Windows/build/uptime, CPU/núcleos, RAM, GPUs, volumes, BIOS, firmware UEFI/BIOS, estado do Secure Boot, TPM, usuário/domínio, adaptadores e endereços IPv4/IPv6.
- Recuperação e gravação opcional do último resultado no histórico SQLite. A preferência de histórico pode ser desligada em **Configurações**.
- Health Score temporário fixo em **95**; não é uma avaliação de saúde real.
- Coleta somente de leitura: esta versão não executa correções, não ativa provedor de IA e não envia o inventário a serviços remotos.

Dados indisponíveis, não suportados pelo firmware ou bloqueados pelo sistema são apresentados como indisponíveis; isso não equivale a um resultado saudável. Consultas WMI e algumas propriedades variam de acordo com edição do Windows, hardware, firmware e permissões do usuário.

## Estrutura

```text
WindowsDoctorAI.sln
├── src/
│   ├── WindowsDoctorAI.App/            # WinUI 3, MVVM, navegação e composição
│   ├── WindowsDoctorAI.Core/           # Portas e contratos
│   ├── WindowsDoctorAI.Domain/         # Inventário, execução e preferências
│   ├── WindowsDoctorAI.Application/    # Caso de uso de diagnóstico
│   ├── WindowsDoctorAI.Infrastructure/# Registro de adaptadores externos
│   ├── WindowsDoctorAI.Diagnostics/    # ComputerInventoryScanner e WMI Windows
│   ├── WindowsDoctorAI.Repair/         # Contratos; sem executores de reparo
│   ├── WindowsDoctorAI.AI/             # Contrato opcional; sem provedor remoto
│   ├── WindowsDoctorAI.Reporting/      # Formatação de inventário
│   └── WindowsDoctorAI.Database/       # EF Core, SQLite e repositórios
└── tests/
    └── WindowsDoctorAI.Tests/          # Testes unitários e integração SQLite
```

Consulte [PROJECT_RULES.md](PROJECT_RULES.md), [CONTRIBUTING.md](CONTRIBUTING.md) e a documentação em [`docs/`](docs/).

## Pré-requisitos

- Windows 10 (build mínimo de plataforma `10.0.17763.0`) ou Windows 11 para executar o aplicativo e realizar a coleta WMI.
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) e conexão à internet na primeira restauração dos pacotes NuGet.
- Visual Studio 2022 atualizado com ferramentas para desenvolvimento desktop .NET recomendadas para WinUI 3 (alternativamente, SDK .NET e Windows App SDK restaurados via NuGet).

A solution foi solicitada em .NET 9. **A política da Microsoft informa fim de suporte do .NET 9 em 10 de novembro de 2026**; o alvo deverá ser reavaliado antes de uma versão de produção.

## Compilar e executar no Windows

Na raiz do repositório, em PowerShell ou Developer Command Prompt:

```powershell
dotnet restore .\WindowsDoctorAI.sln
dotnet build .\WindowsDoctorAI.sln --configuration Release
dotnet test .\tests\WindowsDoctorAI.Tests\WindowsDoctorAI.Tests.csproj --configuration Release
dotnet run --project .\src\WindowsDoctorAI.App\WindowsDoctorAI.App.csproj --configuration Release
```

Ao abrir, selecione **Iniciar Diagnóstico** na tela Inicial. O app cria o banco `windowsdoctorai.db` em `%LOCALAPPDATA%\WindowsDoctorAI\`. O schema inicial é criado com `EnsureCreated`; migrações versionadas ainda não foram introduzidas.

## Testes

A suite não exige um computador físico Windows: testa score, cancelamento e delegação do scanner com uma fonte simulada, coordenação do caso de uso, política de histórico e repositórios EF Core/SQLite em memória.

```powershell
dotnet test .\tests\WindowsDoctorAI.Tests\WindowsDoctorAI.Tests.csproj --configuration Release
```

Isso não substitui validação do build XAML, da navegação, nem testes de integração WMI em Windows. No Linux, `dotnet test` da suite funciona; a compilação WinUI depende do compilador XAML Windows (`XamlCompiler.exe`) e não pode ser concluída nesse sistema.

## Persistência e privacidade

O histórico é ligado por padrão e armazena localmente o inventário da execução, que pode conter identificadores como nome/série do equipamento, usuário e IP. Desative **Salvar diagnósticos no histórico local** para evitar novas gravações. Essa opção não apaga resultados já salvos; remoção de histórico não faz parte deste milestone. SQLite não implica criptografia em repouso. Consulte [docs/Database.md](docs/Database.md) e [docs/Security.md](docs/Security.md).

## Licença

[MIT](LICENSE).
- Botão **Iniciar Diagnóstico** e scanner `ComputerInventoryScanner`, que solicita inventário local de fabricante, modelo, número de série, Windows/build/uptime, CPU/núcleos, RAM, GPUs, modelos de discos físicos e volumes, BIOS, firmware UEFI/BIOS, estado do Secure Boot, TPM, usuário/domínio, adaptadores e endereços IPv4/IPv6.
