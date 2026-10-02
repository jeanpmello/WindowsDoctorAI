# Roadmap

Este roadmap distingue componentes implementados de integração visual, validação e funcionalidades ainda futuras. Código presente não equivale a milestone validado em Windows ou aceito para produção.

## Milestone 1 — Fundação e inventário (implementado)

- Solution .NET 9 em Clean Architecture e desktop WinUI 3/MVVM.
- Inventário local, configurações, SQLite e testes.
- IA e reparos sem provedor/executor ativo.

## Milestone 2 — Diagnostic Engine (implementado; validação Windows pendente)

- Modelo `DiagnosticResult`, contratos e plugins para Windows Update, Services, Drivers, Disk e Event Viewer.
- Execução paralela conforme segurança declarada, isolamento de falhas, medição e Health Score heurístico.
- Dashboard, cobertura, evidências, persistência do relatório e testes com fontes substitutas.
- Scanners somente de leitura; sem modificações de sistema.

Ainda falta validar XAML/WinUI, WUA, Registro, WMI, Event Viewer, permissões, SMART e hardware em Windows.

## Milestone 3 — Knowledge & Repair Platform (base implementada; integração/validação pendentes)

- Modelo de regra com ID/versão, domínio, erros, sintomas, causas, soluções, impacto e referências.
- Importador JSON de schema estrito e limitado; conteúdo é inerte, exige HTTPS em referências e nunca pode registrar comandos/plugins.
- SQLite `PRAGMA user_version = 1`, migração incremental compatível com o schema anterior, histórico de pacotes por hash e versões de regra sem sobrescrita silenciosa.
- Recommendation Engine que liga achados a códigos/sintomas literais, apresenta evidências, impacto declarado e confiança categórica explicada. Sem regra semeada como fato; nenhuma probabilidade artificial.
- Root Cause Analyzer de associações observacionais por identificadores compartilhados. Não determina causa nem sequência temporal.
- Repair Engine com contratos de plugins confiáveis registrados em código, confirmação, risco, auditoria e rollback opcional. A composição não registra reparos reais; plugin de teste/demo é inerte.
- Serviço para relatório HTML local com resumo, Health Score, evidências, recomendações, fontes declaradas e histórico de propostas; saída codificada e sem scripts remotos.
- Testes de unidade e migração SQLite para regras, validação, correspondência, correlação, confirmação, auditoria e HTML.

Não incluído: PDF gerado no app, tela de importação/exportação na WinUI, plugin que modifique o Windows ou regras oficiais preinstaladas. Validação XAML/WinUI e testes em Windows permanecem pendentes.

## Próximas etapas de qualidade

- Definir experiência da interface para importar pacote revisado e salvar/exportar relatório HTML.
- Definir origem assinada/proveniência e critérios revisados para impacto e confiança; avaliar regras com evidência de fontes reconhecidas.
- Exercitar migração em cópia de bancos usados, incluindo rollback da atualização de schema.
- Definir retenção, remoção, compartilhamento e eventual proteção em repouso para relatórios/histórico.

## Visões futuras (fora do Milestone 3)

- **AI Assistant:** explicação opcional, com consentimento e minimização, apoiada em coleta local disponível; não apresentada como diagnóstico certo.
- **Timeline:** sequência histórica só após fontes estruturadas e timestamps confiáveis.
- **Comparador de diagnósticos:** diferenças entre execuções e estado de score/checagens, sem usar comparação como prova de causa.
- **Dashboard corporativo / Enterprise:** agente leve, servidor e painel para múltiplos computadores. Separado do desktop Core e sujeito a arquitetura própria de identidade, acesso, privacidade e auditoria.
- Plugins diagnósticos adicionais e reparos reais são decisões separadas, com permissões e validação próprias.
