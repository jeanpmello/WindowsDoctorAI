# Windows Doctor AI

Aplicativo desktop para Windows em WinUI 3/.NET 9, organizado para diagnóstico local e recuperação assistida. Milestones 1–2 e os componentes centrais do Milestone 3 estão no código; execução visual, APIs Windows e aceitação final ainda exigem validação em uma máquina Windows.

## O que está incluído

- Clean Architecture, app WinUI 3/MVVM, inventário local, configurações, logging e SQLite.
- Cinco scanners de leitura: Windows Update, Services, Drivers, Disk e Event Viewer; falhas e estados indisponíveis não são tratados como saúde.
- Health Score heurístico existente: 100 menos 25 por achado crítico e 8 por aviso, limitado a 0–100; não é calculado sem checagens confirmadas e não representa saúde global.
- Knowledge Engine com modelo de regras e versões, banco SQLite versionado, importador JSON estrito e limitado, Recommendation Engine explicável e Root Cause Analyzer conservador.
- Framework de plugins de reparo com confirmação, risco, auditoria e rollback opcional. **Nenhum plugin que altere o Windows está registrado; não executa comandos, scripts ou mudanças neste milestone.**
- A tela inicial permite selecionar um pacote JSON local, revisar versão, fonte declarada, hash SHA-256 e total de regras antes de importar, e salvar um relatório HTML da execução atual/mais recente. O relatório inclui score, evidências, correlações, recomendações disponíveis e histórico de propostas; PDF não é gerado pelo app.
- IA sem provedor ativo. Nenhum inventário é enviado a serviço externo.

Knowledge Base começa vazia. Não há regra confirmada semeada para `0x80070005` ou outros códigos. A prévia valida schema e limites sem gravar; a importação de um pacote inválido ou conflitante é recusada sem gravação parcial. Versão, fonte e hash são metadados declarados: hash identifica o conteúdo, mas não comprova autoria ou veracidade. Confiança é força do match literal, não probabilidade de causa ou sucesso. O Root Cause Analyzer só aponta identificadores compartilhados e nunca declara causa determinada.

## Estrutura

```text
WindowsDoctorAI.sln
├── src/
│   ├── WindowsDoctorAI.App/             # desktop WinUI 3, MVVM e composição
│   ├── WindowsDoctorAI.Core/            # portas de diagnóstico e persistência
│   ├── WindowsDoctorAI.Domain/          # inventário, regras, resultados e auditoria
│   ├── WindowsDoctorAI.Application/     # scanners orquestrados, conhecimento e avaliação
│   ├── WindowsDoctorAI.Diagnostics/    # plugins locais de leitura
│   ├── WindowsDoctorAI.Reporting/      # relatórios em texto e HTML
│   ├── WindowsDoctorAI.Database/       # EF Core, SQLite e schema versionado
│   ├── WindowsDoctorAI.Infrastructure/# composição de adaptadores locais
│   ├── WindowsDoctorAI.Repair/         # framework; sem executor de sistema ativo
│   └── WindowsDoctorAI.AI/             # contrato opcional; sem provedor
└── tests/WindowsDoctorAI.Tests/        # testes xUnit e integração SQLite
```

**Core** é desktop/local. **Enterprise** (agente + servidor central + painel multi-máquina) é apenas roadmap e não está implementado. Consulte [Architecture](docs/Architecture.md), [Roadmap](docs/Roadmap.md), [Security](docs/Security.md), [Database](docs/Database.md), [PROJECT_RULES.md](PROJECT_RULES.md) e [CONTRIBUTING.md](CONTRIBUTING.md).

## Pré-requisitos e execução em Windows

- Windows 10 (build mínimo `10.0.17763.0`) ou Windows 11 para UI e consultas nativas.
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) e Visual Studio 2022 com ferramentas WinUI/Desktop recomendadas.

Na raiz do repositório, em PowerShell:

```powershell
dotnet restore .\WindowsDoctorAI.sln
dotnet build .\WindowsDoctorAI.sln --configuration Release
dotnet test .\tests\WindowsDoctorAI.Tests\WindowsDoctorAI.Tests.csproj --configuration Release
dotnet run --project .\src\WindowsDoctorAI.App\WindowsDoctorAI.App.csproj --configuration Release
```

O banco `windowsdoctorai.db` é criado em `%LOCALAPPDATA%\WindowsDoctorAI\`. Schema version 1 acompanha as tabelas de conhecimento e auditoria e atualiza bancos anteriores sem substituir histórico existente.

## Testes e limites

Testes unitários cobrem lógica, prévia/importação sem gravação parcial, matches, correlações, confirmação e composição/escape do HTML; testes SQLite cobrem persistência e atualização do schema. Fontes falsas não provam que WUA, WMI, SMART, XAML/WinUI ou Event Viewer funcionem em hardware real.

No Linux é possível testar bibliotecas e SQLite com .NET 9. A validação de XAML depende do compilador do Windows App SDK; build/execução da UI e APIs nativas devem ser verificados em Windows. A edição Enterprise, timeline, comparador, dashboard corporativo, assistente IA e reparos reais continuam no roadmap.

## Persistência e privacidade

O histórico de diagnóstico é local e habilitado por padrão; pode conter nome/série do equipamento, usuário/IP, identificadores Plug and Play e mensagens de eventos. Relatórios HTML repetem evidências. Desativar o histórico impede novas gravações, mas não apaga registros existentes. SQLite não implica criptografia em repouso; consulte [Database](docs/Database.md) e [Security](docs/Security.md).

## Licença

[MIT](LICENSE).
