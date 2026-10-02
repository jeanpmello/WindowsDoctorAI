# Roadmap

Este roadmap distingue componentes implementados de integração visual, validação e funcionalidades ainda futuras. Código presente não equivale a milestone validado em Windows ou aceito para produção.

## Milestone 1 — Fundação e inventário (implementado)

- Solution .NET 9 em Clean Architecture e desktop WinUI 3/MVVM.
- Inventário local, configurações, SQLite e testes.
- IA e reparos sem provedor/executor ativo.

## Milestone 2 — Diagnostic Engine (implementado; validação Windows pendente)

- Modelo `DiagnosticResult`, contratos e plugins para Windows Update, Services, Drivers, Disk, Event Viewer e descoberta de metadados do Windows Server Backup.
- Execução paralela conforme segurança declarada, isolamento de falhas, medição e Health Score heurístico.
- Dashboard, cobertura, evidências, persistência do relatório e testes com fontes substitutas.
- Scanners somente de leitura; sem modificações de sistema.
- O scanner de backup consulta `Get-WBBackupSet` pelo Windows PowerShell existente e mostra apenas contagem, data e tipo minimizados; presença de metadados não comprova integridade nem capacidade de restauração.
- Os testes Linux usam runner falso e fixtures JSON sintéticas. Eles validam parsing/estados e não simulam nem certificam Windows Server 2019 real.

Ainda falta validar XAML/WinUI, WUA, Registro, WMI, Event Viewer, permissões, SMART, hardware e `Get-WBBackupSet` em Windows representativo, inclusive Server 2019.

## Milestone 3 — Knowledge & Repair Platform (fluxos principais implementados; validação Windows pendente)

- Modelo de regra com ID/versão, domínio, erros, sintomas, causas, soluções, impacto e referências.
- Importador JSON de schema estrito e limitado; conteúdo é inerte, exige HTTPS em referências e nunca pode registrar comandos/plugins.
- SQLite `PRAGMA user_version = 1`, migração incremental compatível com o schema anterior, histórico de pacotes por hash e versões de regra sem sobrescrita silenciosa.
- Recommendation Engine que liga achados a códigos/sintomas literais, apresenta evidências, impacto declarado e confiança categórica explicada. Sem regra semeada como fato; nenhuma probabilidade artificial.
- Root Cause Analyzer de associações observacionais por identificadores compartilhados. Não determina causa nem sequência temporal.
- Repair Engine com contratos de plugins confiáveis registrados em código, confirmação, risco, auditoria e rollback opcional. A composição não registra reparos reais; plugin de teste/demo é inerte.
- Serviço e ação WinUI para gerar e salvar relatório HTML local da execução atual/mais recente, com resumo, Health Score, evidências, recomendações e histórico de propostas; saída escapada e sem recursos/scripts remotos.
- Ação WinUI para selecionar pacote JSON local, validar e revisar versão, fonte declarada, hash SHA-256 e total de regras antes da importação; base vazia é explícita e falhas/conflitos não deixam gravação parcial.
- Testes de unidade e migração SQLite para regras, prévia/importação atômica, validação, correspondência, correlação, confirmação, auditoria e composição/escape HTML.

Não incluído: PDF gerado no app, plugin que modifique o Windows, restauração real de backup ou regras oficiais preinstaladas. Qualquer recuperação é uma ação posterior e separada, não executada nem validada nesta etapa; testes end-to-end em Windows permanecem pendentes.

## Próximas etapas de qualidade

- Definir assinatura/proveniência verificável e processo de revisão antes de tratar pacotes/fontes como oficiais; hash atual só identifica bytes e não comprova autoria.
- Definir minimização/redação de evidências e políticas de retenção, remoção, compartilhamento e proteção em repouso para relatórios/histórico.
- Exercitar migração em cópia de bancos usados, incluindo rollback da atualização de schema.

## Visões futuras (fora do Milestone 3)

- **AI Assistant:** explicação opcional, com consentimento e minimização, apoiada em coleta local disponível; não apresentada como diagnóstico certo.
- **Timeline:** sequência histórica só após fontes estruturadas e timestamps confiáveis.
- **Comparador de diagnósticos:** diferenças entre execuções e estado de score/checagens, sem usar comparação como prova de causa.
- **Dashboard corporativo / Enterprise:** agente leve, servidor e painel para múltiplos computadores. Separado do desktop Core e sujeito a arquitetura própria de identidade, acesso, privacidade e auditoria.
- Plugins diagnósticos adicionais e reparos reais são decisões separadas, com permissões e validação próprias.
