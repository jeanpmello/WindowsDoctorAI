# Arquitetura

**Estado:** Milestones 1–2 e os componentes centrais do Milestone 3 estão implementados no repositório. A execução WinUI 3, compilação XAML e APIs nativas ainda precisam de validação em Windows; este documento não representa certificação.

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

`DiagnosticResult` representa a coleta dos scanners; `Unavailable` e `NotVerified` não são estados saudáveis. O Health Score existente é heurístico: 100 menos 25 por achado crítico e 8 por aviso, limitado a 0–100, e não é calculado quando não há verificações confirmadas. Não é uma medida de saúde global.

Os plugins existentes consultam Windows Update, Services, Drivers, Disk e Event Viewer em modo de leitura. Os limites específicos de APIs, permissões, SMART, cobertura e volume permanecem descritos nos resultados e na documentação do Milestone 2.

## Knowledge Engine e recomendações

`KnowledgeRule` modela identificador e versão, domínio, título, impacto, códigos, sintomas, causas, soluções e referências HTTPS. O importador aceita somente o schema JSON `1.0`, com tamanho máximo de 512 KiB, até 500 regras, até 50 itens por lista, limites de texto, enum textual, referências HTTPS e rejeição de propriedades não mapeadas. Caminhos locais, controles e padrões de comando/injeção são rejeitados. O JSON contém dados declarativos; nunca é interpretado como código, script, caminho de execução ou comando.

Não há regras semeadas no banco, inclusive para `0x80070005`; esse identificador só poderá gerar recomendação quando uma regra for explicitamente importada com referências declaradas. O hash SHA-256 identifica o conteúdo importado e permite idempotência, mas **não autentica** a origem. O aplicativo não verifica automaticamente se a URL ou a alegação da regra é confiável.

`RecommendationEngine` só considera resultados com status `Finding`. Ele procura um código literal ou termo de sintoma na evidência coletada, liga cada sugestão aos scanners/trechos que deram match e ordena pelo impacto informado na regra. Confiança é categórica e expressa força da correspondência: texto de sintoma é fraco; código exato em um scanner é moderado; código exato em scanners distintos pode ser forte. Isso não é porcentagem, causalidade, probabilidade de correção nem validação da fonte. Impacto também é metadado declarado pela regra, não uma medição automática.

## Correlação e explicação

`RootCauseAnalyzer` agrupa somente identificadores reconhecíveis repetidos entre fontes de scanner diferentes: referências KB, HRESULTs, IDs explícitos de eventos, IDs de dispositivo e nomes `Nome=` explicitamente observados pelo scanner de serviços. A explicação aponta os resultados originais e o identificador compartilhado. Um identificador coincidente é associação textual, não prova de causa, ordem temporal ou nexo entre update/driver/serviço/evento. Nesta versão `CauseDetermined` permanece falso; sem identificadores suficientes, a causa é indeterminada.

O modelo atual do scanner não fornece uma timeline confiável de eventos históricos entre fontes. Timeline, análise temporal robusta, comparação entre execuções e recomendações causais ficam para outra etapa, após fonte e semântica temporal estruturadas.

## Relatórios

`DiagnosticAssessmentService.CreateHtmlReportAsync` reúne o relatório da execução, recomendações do banco local, correlações e histórico de propostas de reparo. O HTML inclui resumo, Health Score, cobertura/resultados, evidências, impacto/confiança explicados, referências declaradas e histórico. A renderização codifica texto não confiável e não carrega scripts ou estilos remotos.

O serviço HTML está disponível na camada de aplicação, mas ainda não existe fluxo de exportação/seleção de arquivo na interface WinUI. Não há geração PDF nesta entrega; imprimir/salvar como PDF no navegador é uma opção externa, não uma capacidade implementada. Relatórios podem conter evidências locais potencialmente identificáveis; compartilhamento e retenção exigem decisão do usuário.

## Framework de reparos

`RepairEngine` recebe plugins confiáveis registrados em código, exige confirmação booleana explícita para executar ou tentar rollback e grava cada tentativa na auditoria SQLite. O plugin inerte de demonstração e os fakes dos testes não alteram o Windows. A composição do desktop não registra nenhum `IRepairPlugin`; portanto, não há reparo de sistema disponível nem comando/processo executado neste milestone. A importação JSON não pode registrar plugin.

A interface prevê risco, impacto, confirmação e suporte opcional a rollback. Um plugin real futuro exigirá revisão própria de pré-condições, efeito, privilégios, ponto de restauração quando viável, logs sem segredos e testes Windows; a existência do contrato não autoriza sua implementação ou execução.

## Validação pendente

- Compilar a solution e validar XAML/WinUI com Windows App SDK em Windows.
- Exercitar WUA, Registro, WMI, Event Viewer, SMART e permissões em versões e hardware representativos.
- Validar regras importadas com fontes reconhecidas, processo de revisão e critérios de impacto/confiança.
- Definir interface de importação/exportação HTML, retenção/remoção e política de privacidade para relatórios.
- Projetar identidade, autorização, transporte, auditoria e proteção de dados separadamente antes de qualquer edição Enterprise.
