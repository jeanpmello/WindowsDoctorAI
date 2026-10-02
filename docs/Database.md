# Banco de dados

**Estado:** EF Core 9 e SQLite local; o schema tem versão independente, rastreada por `PRAGMA user_version`. A versão atual é 1. A aplicação não usa migrations EF Core geradas; `WindowsDoctorDatabaseMigrator` aplica passos SQL incrementais idempotentes.

## Localização e atualização

A aplicação cria `windowsdoctorai.db` em `%LOCALAPPDATA%\WindowsDoctorAI\`. Inicialização preserva `EnsureCreated` para a fundação original e, em seguida, roda as migrações próprias. Um banco antigo do Milestone 1/2 é tratado como versão 0: as tabelas novas são criadas com `IF NOT EXISTS`, sem apagar execuções ou preferências. Um banco com versão superior à suportada é recusado para evitar downgrade silencioso.

Qualquer alteração futura deve adicionar um próximo passo sequencial, dentro de transação, com teste de migração a partir do schema anterior. Não renumere nem edite migrações já publicadas.

## Tabelas

- **`DiagnosticRuns`** — ID, data de conclusão para ordenação e JSON com execução/inventário/relatório do scanner.
- **`UserSettings`** — preferência local `SaveDiagnosticHistory`.
- **`KnowledgeRules`** — uma linha por `RuleId` + `RuleVersion`; guarda versão do pacote, payload declarativo JSON e data de importação. Os payloads anteriores são preservados; regra com mesmo ID/versão e conteúdo diferente é recusada.
- **`KnowledgeBaseVersions`** — versão do pacote, origem declarada, SHA-256 do arquivo JSON, quantidade de regras e data de importação. O hash permite detectar repetição/conflito de conteúdo; não certifica assinatura, autoria nem validade de referência.
- **`RepairHistory`** — proposta, status, risco declarado, confirmação, suporte a rollback, horários e detalhes limitados. Não há coluna de comando/script.

## Importação e uso

O schema do JSON é estrito e limitado: versão do schema, versão do pacote e regras com campos conhecidos, limite de tamanho/contagem/texto e referências HTTPS. A importação só armazena conteúdo como dados. Não há execução de código, download automático de referências, plugin dinâmico ou regra factual sem importação explícita.

`RecommendationEngine` lê a versão mais recente de cada regra por ID; histórico de versões continua no SQLite. Não há regras semeadas nesta entrega. Uma recomendação preserva o identificador/versão da regra, os scanners e as evidências literais que corresponderam.

## Dados e privacidade

O payload histórico existente pode conter nome/modelo/série do computador, usuário/domínio, endereços de rede, identificadores de dispositivos e mensagens de eventos. Evidências em relatórios podem repetir essas informações. Regras importadas guardam também suas referências e descrições. A origem e o conteúdo de um pacote devem ser revisados antes da importação.

Desabilitar **Salvar diagnósticos no histórico local** impede novas gravações de diagnósticos; não apaga registros existentes. A auditoria de reparo é local. SQLite não fornece criptografia em repouso por si só; não armazenar senhas, tokens ou chaves. Rotina de retenção, exportação e exclusão de histórico ainda não está implementada.
