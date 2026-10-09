# Roadmap

Este roadmap distingue componentes implementados de integração visual, validação e funcionalidades ainda futuras. Código presente e CI que compila/testa a solução não equivalem a execução end-to-end, validação do runtime Windows ou aceitação para produção.

## Milestone 1 — Fundação e inventário (implementado)

- Solution .NET 9 em Clean Architecture e desktop WinUI 3/MVVM.
- Inventário local, configurações, SQLite e testes.
- IA e reparos sem provedor/executor ativo.

## Milestone 2 — Diagnostic Engine (implementado; validação Windows pendente)

- Modelo `DiagnosticResult`, contratos e plugins para Windows Update, Services, Drivers, Disk, Event Viewer e descoberta de metadados do Windows Server Backup.
- Execução paralela conforme segurança declarada, isolamento de falhas, medição e Health Score heurístico.
- Dashboard, cobertura, evidências, persistência do relatório e testes com fontes substitutas.
- Scanners somente de leitura; sem modificações de sistema.
- No Event Viewer genérico, o evento com provider `Microsoft-Windows-WindowsUpdateClient`, channel `Microsoft-Windows-WindowsUpdateClient/Operational` e HRESULT `0x800F0831` é suprimido na origem, independentemente do Event ID; não gera Finding, Recommendation ou estado Healthy nem conteúdo para histórico/HTML. É uma supressão temporária da tupla exata até haver chave confiável de correlação CBS↔EventRecord.
- O scanner consulta `Get-WBBackupSet` pelo Windows PowerShell e mostra metadados minimizados. Separadamente, a UI consulta o catálogo de versões somente sob demanda e exibe ID opaco, data UTC, tipo normalizado e contagem opcional de volumes; a resposta é temporária. O catálogo falha fechado diante de saída inválida ou fora dos limites, mas o contrato dos objetos WSB runtime não está validado nem confirmado em Windows Server 2019; metadados não comprovam integridade ou recuperabilidade.
- Há somente um planejador isolado de prévia sintética: exige conjunto/versão/volume/item conhecidos na lista sintética fornecida, origem e destino alternativo absolutos distintos, bloqueia sobrescrita e fixa a política `CreateCopy`. Não está ligado à UI nem a uma fonte real de itens WSB, não consulta o filesystem e não gera comando copiável.
- Os testes automatizados, inclusive os executados pelo workflow de CI Windows, usam runner falso e fixtures JSON sintéticas de saída normalizada; verificações do script cobrem regras fail-closed, mas não executam PowerShell/WSB real nem validam o contrato dos objetos runtime. A CI compila a solução e roda testes, não é execução end-to-end nem teste em servidor WSB real.

Ainda falta executar visualmente XAML/WinUI e validar WUA, Registro, WMI, Event Viewer, permissões, SMART, hardware e `Get-WBBackupSet` em Windows representativo. Em particular, o catálogo WSB só poderá ser considerado confirmado em Server 2019 após smoke test nesse sistema real.

## Milestone 3 — Knowledge & Repair Platform (fluxos principais implementados; validação Windows pendente)

- Modelo de regra com ID/versão, domínio, erros, sintomas, causas, soluções, impacto e referências.
- Importador JSON de schema estrito e limitado; conteúdo é inerte, exige HTTPS em referências e nunca pode registrar comandos/plugins.
- SQLite `PRAGMA user_version = 4`, migrações SQL incrementais compatíveis com schemas anteriores, histórico de pacotes por hash e versões de regra sem sobrescrita silenciosa.
- Recommendation Engine que liga achados a códigos/sintomas literais, apresenta evidências, impacto declarado e confiança categórica explicada. Sem regra semeada como fato; nenhuma probabilidade artificial.
- Root Cause Analyzer de associações observacionais por identificadores compartilhados. Não determina causa nem sequência temporal.
- Repair Engine com contratos de plugins confiáveis registrados em código, confirmação, risco, auditoria e rollback opcional. A composição não registra reparos reais; plugin de teste/demo é inerte.
- Serviço e ação WinUI para gerar e salvar relatório HTML local da execução atual/mais recente, com resumo, Health Score, evidências, correlações e recomendações; não agrega `RepairHistory` nem histórico de propostas, que permanece no SQLite. Saída escapada e sem recursos/scripts remotos.
- Ação WinUI para selecionar pacote JSON local, validar e revisar versão, fonte declarada, hash SHA-256 e total de regras antes da importação; base vazia é explícita e falhas/conflitos não deixam gravação parcial.
- Testes de unidade e migração SQLite para regras, prévia/importação atômica, validação, correspondência, correlação, confirmação, auditoria e composição/escape HTML.

Não incluído: PDF gerado no app, plugin que modifique o Windows, restauração real de backup ou regras oficiais preinstaladas. A prévia sintética não é suporte de recuperação: não garante recuperação nem integridade e exige validação humana. Restauração real permanece ausente e desabilitada; nenhum backup real foi restaurado ou alterado nesta etapa. O catálogo WSB também aguarda validação runtime em uma VM/host Windows Server 2019.

## Próximas etapas de qualidade

- Definir assinatura/proveniência verificável e processo de revisão antes de tratar pacotes/fontes como oficiais; hash atual só identifica bytes e não comprova autoria.
- Definir minimização/redação de evidências e políticas de retenção, remoção, compartilhamento e proteção em repouso para relatórios/histórico.
- Exercitar migrações em cópias de bancos usados. Já existe teste SQLite em memória que injeta falha na migração v2, verifica rollback transacional e tenta novamente até o schema 4; ele não substitui a validação com cópias de bancos reais.

## Visões futuras (fora do Milestone 3)

- **AI Assistant:** explicação opcional via Ollama local já disponível; contratos para conversa contextual e análise de screenshots foram adicionados, mas a UI conversacional e a busca oficial de soluções ainda precisam ser integradas. A IA não é apresentada como diagnóstico certo e não executa ações.
- **Timeline:** sequência histórica só após fontes estruturadas e timestamps confiáveis.
- **Comparador de diagnósticos:** diferenças entre execuções e estado de score/checagens, sem usar comparação como prova de causa.
- **Dashboard corporativo / Enterprise:** agente leve, servidor e painel para múltiplos computadores. Separado do desktop Core e sujeito a arquitetura própria de identidade, acesso, privacidade e auditoria.
- Plugins diagnósticos adicionais e reparos reais são decisões separadas, com permissões e validação próprias.
