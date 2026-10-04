# Arquitetura

**Estado:** Milestones 1–2 e os componentes centrais do Milestone 3 estão implementados no repositório. O workflow de CI Windows compila a solução e executa testes automatizados, mas não é end-to-end; execução visual, APIs nativas e o contrato runtime do WSB ainda exigem validação em host real. Este documento não representa certificação.

## Produto e fronteira arquitetural

**Windows Doctor AI Core** é a aplicação desktop local para técnicos: inventário, diagnóstico de leitura, conhecimento importado localmente, análise explicável, relatórios e, no futuro, reparos explicitamente aprovados. Os dados permanecem no computador salvo ação deliberada do usuário.

**Windows Doctor AI Enterprise** é uma edição futura separada, prevista para agente leve, servidor central e painel web de inventário/monitoramento. Não há agente, servidor, tenant, sincronização, identidade corporativa, API central nem telemetria nesta implementação. A separação é uma decisão de roadmap, não uma arquitetura já entregue.

```text
WindowsDoctorAI.sln
├── WindowsDoctorAI.App             # desktop WinUI 3, DI e apresentação
├── WindowsDoctorAI.Domain          # inventário, resultados, conhecimento e auditoria
├── WindowsDoctorAI.Core            # portas de scanners, persistência e auditoria
├── WindowsDoctorAI.Application     # diagnóstico, importação, recomendações e composição
├── WindowsDoctorAI.Diagnostics     # plugins locais Windows somente de leitura
├── WindowsDoctorAI.Database        # EF Core, SQLite, repositórios e schema versionado
├── WindowsDoctorAI.Reporting       # relatórios de texto e HTML local
├── WindowsDoctorAI.Repair          # contratos de plugin e barreira de confirmação
├── WindowsDoctorAI.Infrastructure  # composição de adaptadores locais
└── tests/WindowsDoctorAI.Tests     # xUnit, lógica pura e SQLite
```

As dependências continuam apontando para contratos e domínio. `Domain` não conhece WinUI, banco, APIs Windows ou serviços externos. Scanners ficam isolados atrás de contratos substituíveis.

## Diagnóstico e Health Score

`DiagnosticResult` representa a coleta dos scanners; `Unavailable` e `NotVerified` não são estados saudáveis. `SourceMetadata` mantém separadamente providers de evento conhecidos e normalizados; neste incremento, apenas `WindowsUpdateClient` é allowlist. O Health Score existente é heurístico: 100 menos 25 por achado crítico e 8 por aviso, limitado a 0–100, e não é calculado quando não há verificações confirmadas. Não é uma medida de saúde global.

Os plugins existentes consultam Windows Update, Services, Drivers, Disk, Event Viewer e Windows Server Backup em modo de leitura. Os limites específicos de APIs, permissões, SMART, cobertura e volume permanecem descritos nos resultados e na documentação do Milestone 2.

A coleta local consulta `Win32_OperatingSystem.BuildNumber` e `ProductType`. ProductType é convertido somente para enum allowlistado (`Workstation=1`, `DomainController=2`, `Server=3`); desconhecido ou inválido fica null, sem inferência pela Caption. Build é mantido apenas quando inteiro decimal positivo canônico em cultura invariant. Essa coleta de WMI ainda requer validação em host Windows real.

O scanner de Windows Server Backup expõe metadados minimizados. Separadamente, a UI consulta o catálogo de versões somente sob demanda e mostra ID opaco, data UTC, tipo normalizado e contagem de volumes quando disponível; a lista é temporária e não entra no histórico, no HTML ou nos logs. Respostas fora do schema/limites, com IDs repetidos ou formato runtime inesperado descartam o catálogo inteiro (fail-closed), sem exibição parcial. Os testes usam runner falso e JSON sintético; o contrato runtime ainda não foi validado em Windows Server 2019.

`SyntheticFileRecoveryPreviewPlanner` permanece deliberadamente isolado: valida seleções contra dados sintéticos fornecidos pelo chamador e produz somente texto/modelo descritivo com política fixa `CreateCopy`. Não está registrado na composição nem na UI, não consulta backup ou filesystem, não gera comando e não executa recuperação. O catálogo de metadados não fornece suporte de restauração.

## Knowledge Engine e recomendações

`KnowledgeRule` modela identificador e versão, domínio, título, impacto, códigos, sintomas, causas, soluções e referências HTTPS. O importador aceita schemas JSON `1.0` a `1.4`, limitado a 512 KiB/500 regras, com listas/texto limitados, enum textual, referências HTTPS e rejeição de propriedades não mapeadas. Regras estritas declaram código HRESULT exato, scanner, contexto específico no mesmo achado, evidência requerida e procedimento manual com privilégio/risco/backup/rollback/limitação da fonte. `Applicability` continua texto explicativo e não é um filtro automático. O schema 1.2 permite providers estruturados canônicos da allowlist; 1.3 permite tipos de evidência estruturada da allowlist; 1.4 exige para cada regra `osTarget` explícito, famílias enum `WindowsClient`/`WindowsServer` e builds mínimo/máximo opcionais, positivos e inclusivos, rejeitando faixa invertida e famílias duplicadas. O alvo é dado declarativo, nunca código, caminho de execução ou comando.

Não há regras semeadas no banco, inclusive para `0x80070005`; esse identificador só poderá gerar recomendação quando uma regra for explicitamente importada com referências declaradas. O hash SHA-256 identifica o conteúdo importado e permite idempotência, mas **não autentica** a origem. O aplicativo não verifica automaticamente se a URL ou a alegação da regra é confiável.

`RecommendationEngine` só considera resultados com status `Finding`. Regras 1.0–1.3 mantêm o matching legado e a aplicabilidade em texto dessas regras não é avaliada automaticamente; regras estritas continuam exigindo, no mesmo achado, código exato, scanner designado e condições declaradas. Regras 1.4 também exigem inventário redigido/estruturado do próprio run: ProductType conhecido e build canônico positivo dentro da família e faixa alvo; inventário, ProductType ou build ausente/inválido falha fechado apenas para essas regras. 1=Workstation mapeia para `WindowsClient`; 2=DomainController e 3=Server mapeiam para `WindowsServer`. Termos textuais são comparados ao texto redigido, enquanto providers são comparados exclusivamente a `SourceMetadata` normalizado. Cada recomendação distingue alvo estruturado verificado de aplicabilidade legada não verificada, sem mostrar os valores locais nem inferir causalidade. Confiança continua medindo somente força do match literal; impacto é editorial, não medição.

## Correlação e explicação

`RootCauseAnalyzer` agrupa somente identificadores reconhecíveis repetidos entre fontes de scanner diferentes: referências KB, HRESULTs, IDs explícitos de eventos, IDs de dispositivo e nomes `Nome=` explicitamente observados pelo scanner de serviços. A explicação aponta os resultados originais e o identificador compartilhado. Um identificador coincidente é associação textual, não prova de causa, ordem temporal ou nexo entre update/driver/serviço/evento. Nesta versão `CauseDetermined` permanece falso; sem identificadores suficientes, a causa é indeterminada.

O modelo atual do scanner não fornece uma timeline confiável de eventos históricos entre fontes. Timeline, análise temporal robusta, comparação entre execuções e recomendações causais ficam para outra etapa, após fonte e semântica temporal estruturadas.

## Relatórios

`DiagnosticAssessmentService.CreateHtmlReportAsync` reúne o relatório da execução, recomendações do banco local e correlações. O HTML inclui resumo, Health Score, cobertura/resultados, evidências, impacto/confiança explicados e referências declaradas; não consulta nem agrega `RepairHistory`, que permanece na trilha SQLite sem vínculo confiável com a execução diagnóstica. A renderização codifica texto não confiável e não carrega scripts ou estilos remotos.

A interface WinUI já permite gerar e salvar HTML da execução atual ou mais recente, no destino escolhido pelo usuário. Não há geração PDF nesta entrega; imprimir/salvar como PDF no navegador é uma opção externa, não uma capacidade implementada. Relatórios podem conter evidências locais potencialmente identificáveis; compartilhamento e retenção exigem decisão do usuário.

## Framework de reparos

`RepairEngine` recebe plugins confiáveis registrados em código. A nova confirmação é um objeto imutável vinculado a `RepairId`, versão, risco, alvo, ação e fingerprint do plano; um booleano legado nunca autoriza execução. Cada tentativa recebe `RepairExecutionId` e é gravada no SQLite antes da chamada ao plugin. O plugin precisa persistir o marco de início antes de qualquer efeito; falhas e cancelamentos são finalizados sem reutilizar o token cancelado. Se o processo parar após o marco, o último estado auditado permanece para revisão.

Rollback só pode referenciar um `RepairExecutionId` original concluído com sucesso, exige consentimento separado para a ação de rollback e registra seu próprio ID ligado à origem; exceções de rollback também são auditadas. Pré/pós-condições são declarações nos tipos, não verificações automáticas: o resultado de pós-condições começa como `NotEvaluated`, e não há checagem interna do Windows. Plugins antigos ainda compilam, mas o adaptador conservador marca seu início antes de chamá-los; rollback legado sem ID não é usado.

O plugin inerte de demonstração e os fakes determinísticos dos testes não alteram o Windows. A composição do desktop não registra nenhum `IRepairPlugin`; portanto, não há reparo de sistema disponível nem comando/processo executado neste milestone. A importação JSON não pode registrar plugin. A existência dos contratos não autoriza implementar ou executar reparo real.

Um plugin real futuro exigirá revisão própria de pré-condições, verificação de pós-condições, alvo, efeito, privilégios mínimos, ponto de restauração quando viável, logs sem segredos e testes em Windows isolado; esta fatia não valida APIs, resultados ou logs do Windows.

## Validação pendente

- Executar visualmente a UI WinUI e exercitar WUA, Registro, WMI, Event Viewer, SMART e permissões em versões e hardware representativos; a compilação da solução já faz parte do workflow de CI Windows.
- Validar a consulta real `Get-WBBackupSet` e os tipos dos objetos WSB em Windows Server 2019; a suíte atual usa runner falso e fixtures sintéticas e não é E2E.
- Validar regras importadas com fontes reconhecidas, processo de revisão e critérios de impacto/confiança.
- Revisar retenção/remoção e política de privacidade para relatórios, inclusive antes de compartilhá-los.
- Projetar identidade, autorização, transporte, auditoria e proteção de dados separadamente antes de qualquer edição Enterprise.
