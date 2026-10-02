# Changelog

Mudanças relevantes do Windows Doctor AI.

## [Unreleased] — Milestone 3: Knowledge & Repair Platform

### Added

- Modelo declarativo de conhecimento com versões de regras, erros, sintomas, causas, soluções, impacto e referências.
- Importador JSON estrito e limitado; regras permanecem inertes, e o SHA-256 serve à identificação/idempotência, não à autenticação da origem.
- Schema SQLite versionado por `PRAGMA user_version`, compatível com bancos dos Milestones 1–2.
- Recommendation Engine com correspondências literais, evidências rastreáveis e confiança categórica explicada; nenhuma regra factual pré-semeada.
- Root Cause Analyzer com associações por identificadores compartilhados; não determina causa ou cronologia.
- Framework de reparo com confirmação, risco, auditoria e rollback contratual. Nenhum plugin de modificação do Windows está registrado.
- Serviço local de relatório HTML com resumo, score, evidências, recomendações, referências e histórico; sem PDF ou fluxo de exportação visual.
- Testes de unidade e SQLite para importação, versão do schema, confiança, correlação, confirmação, histórico e escaping HTML.
- README, arquitetura, roadmap, segurança e documentação do banco atualizados.

### Notes

- Regras importadas e referências são declaradas; o aplicativo não verifica automaticamente sua autenticidade ou validade.
- WinUI/XAML e APIs Windows continuam dependendo de validação em uma máquina Windows; testes em Linux não comprovam esse comportamento.
- A edição Enterprise, AI Assistant, timeline, comparação de diagnósticos, dashboard corporativo e reparos reais permanecem no roadmap.
