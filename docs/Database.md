# Banco de dados

**Estado:** EF Core 9 e SQLite local; o schema tem versão independente, rastreada por `PRAGMA user_version`. A versão atual é 4. A aplicação não usa migrations EF Core geradas; `WindowsDoctorDatabaseMigrator` aplica passos SQL incrementais idempotentes.

## Localização e atualização

A aplicação cria `windowsdoctorai.db` em `%LOCALAPPDATA%\WindowsDoctorAI\`. Inicialização preserva `EnsureCreated` para a fundação original e, em seguida, roda as migrações próprias. Um banco antigo do Milestone 1/2 é tratado como versão 0: as tabelas novas são criadas com `IF NOT EXISTS`, sem apagar execuções ou preferências. Um banco com versão superior à suportada é recusado para evitar downgrade silencioso.

O passo 3 adiciona `UserSettings.DiagnosticRetentionDays` como inteiro `NOT NULL DEFAULT 0`; a migração também cria `UserSettings` com defaults caso um banco legado mínimo não tenha a tabela. O zero significa retenção desativada; nenhuma linha histórica é apagada pela migração.

O passo 4 define o histórico como opt-in: atualiza `UserSettings.SaveDiagnosticHistory` para `false` em instalações existentes, pois o `true` anterior era seed/default e não comprova consentimento afirmativo. O passo preserva todas as linhas de `DiagnosticRuns`, seus payloads, e o prazo já selecionado. Uma nova gravação requer que o usuário habilite a opção.

Qualquer alteração futura deve adicionar um próximo passo sequencial, dentro de transação, com teste de migração a partir do schema anterior. Não renumere nem edite migrações já publicadas.

O teste `FailedMigrationFromSchemaV2RollsBackCompletelyAndCanBeRetried` usa SQLite em memória e injeta uma falha sintética durante a atualização a partir do schema 2. Verifica que a transação reverte integralmente (versão, objetos e linhas legadas preservados) e que, removida a falha, a nova tentativa conclui até o schema 4. É cobertura de rollback/retry de migração, não uma simulação de falha física ou corrupção do arquivo de banco.

## Tabelas

- **`DiagnosticRuns`** — ID, data de conclusão para ordenação e JSON com execução/inventário/relatório do scanner.
- **`UserSettings`** — `SaveDiagnosticHistory` (opt-in, `false` por padrão) e `DiagnosticRetentionDays` (`0` conserva indefinidamente; prazo ativo é expresso em dias).
- **`KnowledgeRules`** — uma linha por `RuleId` + `RuleVersion`; guarda versão do pacote, payload declarativo JSON e data de importação. Os payloads anteriores são preservados; regra com mesmo ID/versão e conteúdo diferente é recusada.
- **`KnowledgeBaseVersions`** — versão do pacote, origem declarada, SHA-256 do arquivo JSON, quantidade de regras e data de importação. O hash permite detectar repetição/conflito de conteúdo; não certifica assinatura, autoria nem validade de referência.
- **`RepairHistory`** — uma linha por `RepairExecutionId` (o ID primário existente), com proposta, status, risco declarado, confirmação e detalhes limitados. `AuditMetadataJson` acrescenta versão/alvo/condições declaradas, hash do plano, ID e horário do consentimento, ação, início efetivo, estado da verificação pós-condições e `RelatedRepairExecutionId` em rollback. Não há coluna de comando/script.

A migração 2 adiciona `AuditMetadataJson` com valor padrão `{}` e preserva linhas do schema 1. A migração 3 adiciona a preferência de retenção com default desativado, cria a tabela `UserSettings` quando ausente e preserva registros/preferências existentes. A migração 4 desabilita o default implícito de gravação, sem excluir histórico. O repositório de reparos grava a preparação antes de chamar um plugin e atualiza a mesma linha ao sinalizar início e ao concluir; uma interrupção após o início deixa o último estado persistido para revisão. Se a gravação prévia falhar, o plugin não é chamado.

## Importação e uso

O JSON aceita schema 1.0 para compatibilidade, schema 1.1 para regras estritas, schema 1.2 para providers estruturados allowlist, schema 1.3 para tipos de evidência estruturada allowlist e schema 1.4 para alvo OS+build estruturado obrigatório por regra. Esse alvo usa enum de família `WindowsClient`/`WindowsServer` e builds inteiros positivos inclusivos opcionais; `Applicability` em texto livre é apenas explicativo. Regras 1.0–1.3 continuam com o comportamento de match anterior e sua aplicabilidade textual não é verificada automaticamente. Para 1.4, inventário ausente/desconhecido/malformado falha fechado. Pacotes continuam limitados a 512 KiB/500 regras; importação inválida é rejeitada sem gravação parcial.

O piloto Microsoft está em `knowledge-packs/microsoft-windows-update-pilot.json`, mas não é carregado pelo banco nem pelo startup e permaneceu inalterado. Para usá-lo, selecione esse arquivo, revise versão/fonte/hash/quantidade e importe deliberadamente; a base permanece vazia até essa ação. Alvos OS+build de regras novas residem no `PayloadJson` existente, portanto não adicionam colunas nem alteram `PRAGMA user_version`.

`RecommendationEngine` lê a versão mais recente de cada regra por ID; histórico de versões continua no SQLite. Não há regras semeadas. `0x80073712` conserva seus critérios e orientação preexistentes. `0x800F0831` não gera `Finding` nem recomendação: a regra foi removida do pack piloto, e o importador e o motor de recomendação também rejeitam/bloqueiam regras para esse código até que exista uma chave confiável que associe o evento a um registro CBS.

A importação CBS é manual e offline, sem depender de execução diagnóstica, evento atual ou timestamp. O serviço lê até 2 MiB em memória e classifica somente tipos genéricos de marcador; ignora tempos, fuso/DST, idade, pacote e quaisquer coincidências com eventos. O resultado é uma observação de UI não acionável, explicitamente “não atribuída ao evento; não confirma causa; não acionável”. Não é `DiagnosticResult`, recomendação ou evidência tipada do histórico; não é anexada à execução nem gravada em SQLite, HTML ou logs. Texto bruto, package identity, caminhos, hostname e outros campos livres não são expostos ou persistidos.

## Dados e privacidade

Payloads antigos podem conter valores não minimizados; a migração os preserva. Novas gravações passam por `DiagnosticPrivacyRedactor`, minimizando campos textuais, identificadores, e-mails, caminhos, IPs, GUIDs e atribuições comuns de credenciais. O único campo novo de inventário persistido é o enum allowlistado `OperatingSystem.ProductType`, necessário à avaliação local de regra 1.4; `Build` já era parte do inventário. A checagem usa o build local em memória e não grava um estado de verificação/recomendação adicional. Exibição de achados e relatórios aplicam redação também a payloads legados, sem reescrevê-los no banco.

A consulta sob demanda do catálogo de versões WSB mantém somente um snapshot temporário em memória; ID opaco, data UTC, tipo e contagem opcional de volumes não são gravados em `DiagnosticRuns`/`RepairHistory`, no HTML ou nos logs. A presença no catálogo não representa recuperação nem altera o schema SQLite.

Desabilitar **Salvar diagnósticos no histórico local** impede novas gravações, mas não apaga registros existentes. Retenção é independente e conserva por padrão (`0`); a UI oferece 30, 90, 180 ou 365 dias. Um prazo diferente de zero só é salvo após confirmação explícita. A confirmação autoriza o expurgo imediato das linhas de `DiagnosticRuns` cuja conclusão UTC seja anterior ao limite; cancelamento não salva as preferências nem remove dados. O aplicativo não expurga por idade ao iniciar. **Apagar todo o histórico** também exige confirmação e remove todas as linhas de `DiagnosticRuns` dentro de transação; não afeta arquivos HTML já exportados, `RepairHistory`, preferências ou base de conhecimento. Arquivos HTML pré-existentes permanecem fora da retenção e não são apagados automaticamente. A operação SQLite é exclusão lógica da linha, não sobrescrita segura do espaço livre. SQLite não fornece criptografia em repouso por si só; não armazenar senhas, tokens ou chaves.
