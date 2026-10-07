Attempting to perform the InitializeDefaultDrives operation on the 'FileSystem' provider failed.
# Changelog

Mudanças relevantes do Windows Doctor AI.

## [Unreleased] — Milestone 3: Knowledge & Repair Platform

### Hotfix local — 0x800F0831 / CBS sem correlação

- Suprimido Finding para o HRESULT `0x800F0831`; regras de conhecimento com esse código são rejeitadas e a recomendação permanece bloqueada.
- Removidas associações CBS↔EventRecord, timestamp-match e package identity. Importação manual agora classifica somente tipos genéricos em memória e exibe disclaimer não atribuível/não acionável, sem histórico, HTML, banco ou logs.
- Testes sintéticos cobrem eventos isolados/múltiplos/antigos/DST-like, marcadores sem evento e same-second sem Finding/Recommendation.

### Added

- Modelo declarativo de conhecimento com versões de regras, erros, sintomas, causas, soluções, impacto e referências.
- Importador JSON estrito e limitado; regras permanecem inertes, e o SHA-256 serve à identificação/idempotência, não à autenticação da origem.
- Schema SQLite versionado por `PRAGMA user_version`, compatível com bancos dos Milestones 1–2.
- Recommendation Engine com correspondências literais, evidências rastreáveis e confiança categórica explicada; nenhuma regra factual pré-semeada.
- Root Cause Analyzer com associações por identificadores compartilhados; não determina causa ou cronologia.
- Framework de reparo com confirmação, risco, auditoria e rollback contratual. Nenhum plugin de modificação do Windows está registrado.
- Serviço e ação WinUI para salvar relatório HTML local da execução atual ou mais recente, com resumo, score, evidências, correlações e recomendações correspondentes às regras importadas. O relatório não agrega `RepairHistory` nem histórico de propostas; não há PDF gerado pelo aplicativo.
- Testes de unidade e SQLite para importação, versão do schema, confiança, correlação, confirmação, histórico e escaping HTML.
- Metadata `SourceMetadata` normalizado/allowlist para provider do Event Log, matching do piloto por campo estruturado, e redaction uniforme da mensagem bruta em SQLite, UI e HTML; schema do pacote atualizado para 1.2.
- Coleta local de `Win32_OperatingSystem.ProductType` como enum allowlistado e validação do `BuildNumber` decimal canônico; schema 1.4 exige alvo OS+build para regras novas e a avaliação falha fechado quando o inventário é desconhecido.
- Relatório distingue alvo estruturado verificado de aplicabilidade textual legada não verificada; sem alegação causal ou exposição de ProductType/build na explicação.
- README, arquitetura, roadmap, segurança e documentação do banco atualizados.

### Notes

- Regras importadas e referências são declaradas; o aplicativo não verifica automaticamente sua autenticidade ou validade.
- WinUI/XAML e APIs Windows continuam dependendo de validação em uma máquina Windows; testes em Linux não comprovam esse comportamento.
- A edição Enterprise, AI Assistant, timeline, comparação de diagnósticos, dashboard corporativo e reparos reais permanecem no roadmap.

