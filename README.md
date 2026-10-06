# Windows Doctor AI

Aplicativo desktop para Windows em WinUI 3/.NET 9, organizado para diagnóstico local e recuperação assistida. Milestones 1–2 e os componentes centrais do Milestone 3 estão no código. O workflow de CI no Windows compila a solução e executa a suíte automatizada, mas isso não é validação end-to-end: execução visual e APIs nativas ainda exigem teste em host real.

## O que está incluído

- Clean Architecture, app WinUI 3/MVVM, inventário local, configurações, logging e SQLite.
- Scanners de leitura para Windows Update, Services, Drivers, Disk, Event Viewer e Windows Server Backup; falhas e estados indisponíveis não são tratados como saúde.
- O catálogo de versões WSB é consultado somente quando solicitado na UI e exibe apenas ID opaco, data UTC, tipo normalizado e contagem de volumes quando disponível. Respostas inválidas ou fora dos limites descartam a lista inteira (fail-closed); o contrato runtime não foi validado em Windows Server 2019 e não há recuperação real.
- Health Score heurístico existente: 100 menos 25 por achado crítico e 8 por aviso, limitado a 0–100; não é calculado sem checagens confirmadas e não representa saúde global.
- Knowledge Engine com modelo versionado, importador JSON estrito (schemas 1.0 a 1.4), contexto textual, providers e evidências estruturadas allowlist, alvo OS+build para regras novas, Recommendation Engine explicável e Root Cause Analyzer conservador.
- Framework de plugins de reparo com confirmação, risco, auditoria e rollback opcional. **Nenhum plugin que altere o Windows está registrado; não executa comandos, scripts ou mudanças neste milestone.**
- A tela inicial permite selecionar um pacote JSON local, revisar versão, fonte declarada, hash SHA-256 e total de regras antes de importar, e salvar um relatório HTML da execução atual/mais recente. O relatório inclui score, evidências, correlações e recomendações correspondentes a regras importadas; não agrega `RepairHistory` nem histórico de propostas. PDF não é gerado pelo app.
- Análise por IA **local** opcional via [Ollama](https://ollama.com): ao iniciar a análise, o app envia ao endpoint Ollama configurado em endereço loopback (`localhost`, `127.0.0.1` ou `::1`) os textos de instrução e do diagnóstico exibidos na prévia e mostra a explicação/priorização. Identificadores conhecidos são redigidos, mas isso não garante anonimização completa. A requisição Ollama usa cliente sem proxy/redirecionamento e só aceita loopback; o app não controla o que o próprio serviço local faz com o conteúdo (por exemplo, processamento ou armazenamento). A IA só gera texto; não executa correções e pode errar. Os textos usados ficam visíveis na tela. Configuração em `appsettings.json` (`Ai:Ollama`: `Enabled`, `Model`, `BaseUrl`); endereços fora do loopback são recusados. Requer o Ollama instalado e um modelo baixado (ex.: `ollama pull llama3.1:8b`).

Knowledge Base começa vazia. Nenhuma regra é semeada automaticamente; o pacote piloto `knowledge-packs/microsoft-windows-update-pilot.json` só entra no banco depois que a pessoa o selecionar, revisar e importar pela interface. Ele traz duas regras: `0xC1900107` exige código exato no mesmo achado que contexto de Windows Setup; `0x80073712` exige código exato, scanner Windows Update e o provider estruturado allowlist `WindowsUpdateClient` no mesmo achado. A mensagem bruta não pode satisfazer esse provider. Códigos soltos, contexto textual genérico e combinações espalhadas por achados diferentes não ativam essas regras.

A prévia valida schema e limites sem gravar; pacotes inválidos ou conflitantes são recusados sem gravação parcial. O schema 1.0 continua aceito para compatibilidade; 1.1 exige condição estrita e metadados de evidência/procedimento, 1.2 permite provider estruturado allowlist, 1.3 permite tipo de evidência estruturada allowlist e 1.4 exige `osTarget` explícito por regra, com famílias `WindowsClient`/`WindowsServer` e intervalo opcional de builds positivos inclusivos. `Applicability` em texto livre é explicativo e nunca filtra. Para regras 1.4, o matcher só recomenda quando o inventário contém `ProductType` conhecido e build decimal positivo canônico dentro do alvo; unknown/ausente/malformado falha fechado. Regras 1.0–1.3 mantêm o comportamento anterior e sua aplicabilidade textual não é verificada automaticamente. A coleta usa `Win32_OperatingSystem.ProductType` documentado pela [Microsoft](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-operatingsystem): 1 é estação cliente, 2 controlador de domínio e 3 servidor; 2 e 3 mapeiam a `WindowsServer`. Valor desconhecido permanece null, sem inferência por `Caption`. O único novo campo de inventário local é `ProductType`; a redação existente permanece aplicada e os valores concretos de ProductType/build não são adicionados à explicação da recomendação. O pacote piloto permanece inalterado. A curadoria parte de páginas Microsoft de primeira parte, mas publisher e URLs não têm autenticidade criptográfica verificada. Versão, fonte e hash são declarações/metadados; hash identifica conteúdo, não autoria ou veracidade. Nenhuma regra executa código ou comando: reiniciar e repetir Setup exige confirmação do usuário e é manual; DISM/SFC é orientação manual, elevada e modificadora, sem plugin e sem rollback documentado. Confiança é força do match literal, não probabilidade de causa ou sucesso. O Root Cause Analyzer só aponta identificadores compartilhados e nunca declara causa determinada.

`0x800F0831` não produz Finding ou Recommendation: a regra foi retirada do pacote piloto, e o importador e o Recommendation Engine bloqueiam esse código enquanto não existir uma chave confiável de associação entre EventRecord e CBS. A importação manual de `CBS.log` é uma observação offline independente: só classifica o tipo genérico de marcador, não exige evento/timestamp, não revela pacote e mostra o aviso “não atribuída ao evento; não confirma causa; não acionável”. O conteúdo não entra em histórico, HTML, logs ou banco e não aciona reparos.

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

## Alpha de diagnóstico — Windows 11 x64

A distribuição inicial é um artefato ZIP unpackaged self-contained, sem assinatura e sem instalador. Consulte [o guia da Alpha](docs/alpha-windows-11-x64.md) para baixar o artefato do workflow manual, executar o app, conferir a dependência do Visual C++ Redistributable e conhecer os limites de validação. Esta build não declara suporte a Windows Server nem a outras arquiteturas.

## Pré-requisitos para desenvolvimento em Windows

- Windows 10 (build mínimo `10.0.17763.0`) ou Windows 11 para UI e consultas nativas.
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) e Visual Studio 2022 com ferramentas WinUI/Desktop recomendadas.

Na raiz do repositório, em PowerShell:

```powershell
dotnet restore .\WindowsDoctorAI.sln
dotnet build .\WindowsDoctorAI.sln --configuration Release
dotnet test .\tests\WindowsDoctorAI.Tests\WindowsDoctorAI.Tests.csproj --configuration Release
dotnet run --project .\src\WindowsDoctorAI.App\WindowsDoctorAI.App.csproj --configuration Release
```

O banco `windowsdoctorai.db` é criado em `%LOCALAPPDATA%\WindowsDoctorAI\`. Schema version 4 acompanha as tabelas de conhecimento, auditoria e retenção; as migrações preservam as execuções existentes e desligam o default implícito de gravação.

## Testes e limites

Testes unitários cobrem lógica, prévia/importação sem gravação parcial, matches, correlações, confirmação e composição/escape do HTML; testes SQLite cobrem persistência e atualização do schema. Há um teste de falha de migração v2 que verifica rollback transacional e retry até o schema 4, usando SQLite em memória e falha sintética.

O workflow de CI Windows compila a solução com `msbuild` (incluindo WinUI/XAML) e executa os testes xUnit. Os testes WSB usam runner falso e fixtures JSON sintéticas: não são end-to-end, não consultam objetos WSB runtime e não validam `Get-WBBackupSet` em Windows Server 2019. A execução visual da UI e as APIs nativas ainda precisam de teste em host real; nenhuma restauração de backup real foi implementada ou validada. A edição Enterprise, timeline, comparador, dashboard corporativo, assistente IA e reparos reais continuam no roadmap.

## Persistência e privacidade

O histórico de diagnóstico é local e opt-in, desativado por padrão; novas gravações redigem os campos textuais de resultados e identificadores conhecidos. A retenção conserva por padrão e qualquer expurgo por idade exige confirmação explícita; apagar histórico SQLite não remove arquivos HTML exportados nem o histórico de reparos. Registros antigos são preservados, mas exibição de achados e novos relatórios HTML aplicam redação. Arquivos HTML existentes ficam fora da retenção e não são apagados automaticamente. SQLite não implica criptografia em repouso nem apagamento físico seguro; consulte [Database](docs/Database.md) e [Security](docs/Security.md).

## Licença

[MIT](LICENSE).
