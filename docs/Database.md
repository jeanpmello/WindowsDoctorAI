# Banco de dados

**Estado:** EF Core 9 e SQLite local; o schema tem versão independente, rastreada por `PRAGMA user_version`. A versão atual é 3. A aplicação não usa migrations EF Core geradas; `WindowsDoctorDatabaseMigrator` aplica passos SQL incrementais idempotentes.

## Localização e atualização

A aplicação cria `windowsdoctorai.db` em `%LOCALAPPDATA%\WindowsDoctorAI\`. Inicialização preserva `EnsureCreated` para a fundação original e, em seguida, roda as migrações próprias. Um banco antigo do Milestone 1/2 é tratado como versão 0: as tabelas novas são criadas com `IF NOT EXISTS`, sem apagar execuções ou preferências. Um banco com versão superior à suportada é recusado para evitar downgrade silencioso.

O passo 3 adiciona `UserSettings.DiagnosticRetentionDays` como inteiro `NOT NULL DEFAULT 0`; a migração também cria `UserSettings` com defaults caso um banco legado mínimo não tenha a tabela. Preferências preexistentes e payloads de execuções são preservados. O zero significa retenção desativada; nenhuma linha histórica é apagada pela migração.

Qualquer alteração futura deve adicionar um próximo passo sequencial, dentro de transação, com teste de migração a partir do schema anterior. Não renumere nem edite migrações já publicadas.

## Tabelas

- **`DiagnosticRuns`** — ID, data de conclusão para ordenação e JSON com execução/inventário/relatório do scanner.
- **`UserSettings`** — `SaveDiagnosticHistory` e `DiagnosticRetentionDays` (0 conserva indefinidamente; prazo ativo é expresso em dias).
- **`KnowledgeRules`** — uma linha por `RuleId` + `RuleVersion`; guarda versão do pacote, payload declarativo JSON e data de importação. Os payloads anteriores são preservados; regra com mesmo ID/versão e conteúdo diferente é recusada.
- **`KnowledgeBaseVersions`** — versão do pacote, origem declarada, SHA-256 do arquivo JSON, quantidade de regras e data de importação. O hash permite detectar repetição/conflito de conteúdo; não certifica assinatura, autoria nem validade de referência.
- **`RepairHistory`** — uma linha por `RepairExecutionId` (o ID primário existente), com proposta, status, risco declarado, confirmação e detalhes limitados. `AuditMetadataJson` acrescenta versão/alvo/condições declaradas, hash do plano, ID e horário do consentimento, ação, início efetivo, estado da verificação pós-condições e `RelatedRepairExecutionId` em rollback. Não há coluna de comando/script.

A migração 2 adiciona `AuditMetadataJson` com valor padrão `{}` e preserva linhas do schema 1. A migração 3 adiciona a preferência de retenção com default desativado, cria a tabela `UserSettings` quando ausente e preserva registros/preferências existentes. O repositório de reparos grava a preparação antes de chamar um plugin e atualiza a mesma linha ao sinalizar início e ao concluir; uma interrupção após o início deixa o último estado persistido para revisão. Se a gravação prévia falhar, o plugin não é chamado.

## Importação e uso

O JSON aceita schema 1.0 para compatibilidade e schema 1.1 para regras com aplicabilidade, condição de match contextual estrita, evidências necessárias e procedimento documentado. O 1.1 exige código HRESULT exato, scanner explícito e termos de contexto no mesmo achado; recusa código genérico, condição vazia/genérica e campos de comando/script. Pacotes continuam limitados a 512 KiB/500 regras, listas/texto limitados e referências HTTPS. A importação só armazena conteúdo como dados: não há execução de código, download de referências, plugin dinâmico ou regra ativa antes de importação explícita.

O piloto Microsoft está em `knowledge-packs/microsoft-windows-update-pilot.json`, mas não é carregado pelo banco nem pelo startup. Para usá-lo, abra a interface, selecione esse arquivo, revise versão/fonte/hash/quantidade e importe deliberadamente; a base permanece vazia até essa ação. A curadoria deriva das páginas Microsoft indicadas no próprio arquivo; URLs e publisher alegado não têm autenticidade criptográfica verificada. A extensão fica dentro do `PayloadJson` existente, portanto não altera colunas nem `PRAGMA user_version`.

`RecommendationEngine` lê a versão mais recente de cada regra por ID; histórico de versões continua no SQLite. Não há regras semeadas. O piloto requer contexto junto do código exato; código isolado não ativa suas regras. Recomendações preservam identificador/versão, scanner e evidência literal. Ações são instruções declarativas: reinicialização exige confirmação manual; DISM é privilegiado/modificador, SFC só é indicado após sucesso do DISM e o rollback está indisponível conforme as fontes.

## Dados e privacidade

Payloads antigos podem conter valores não minimizados, inclusive nome/série do computador, usuário/domínio, endereços IP e mensagens de eventos; a migração os preserva. As novas gravações passam por `DiagnosticPrivacyRedactor`, que redige `ComputerName`, `SerialNumber`, `UserName`, `Domain` e `Bios.SerialNumber`, limpa endereços IPv4/IPv6 de inventário/adaptadores e substitui o corpo de descrições dos eventos de `Event Viewer`/falhas de `Windows Update`. Códigos de erro no formato HRESULT `0x` + oito dígitos, log/ID/nível/horário e outras evidências estruturadas são preservados. A exportação HTML aplica a mesma redação também a payloads legados, sem alterá-los no banco. Textos declarativos de regras importadas e propostas de reparo não são tratados como fontes confiáveis; não armazenar credenciais e revisar antes de compartilhar.

Desabilitar **Salvar diagnósticos no histórico local** impede novas gravações, mas não apaga registros existentes. Retenção é opt-in e desligada por padrão (`0`); a UI oferece 30, 90, 180 ou 365 dias. Quando ativa, apaga somente linhas de `DiagnosticRuns` cuja conclusão UTC seja anterior ao prazo, ao iniciar o app e ao salvar preferências. **Apagar todo o histórico** exige confirmação e remove todas as linhas de `DiagnosticRuns` dentro de transação; não afeta arquivos HTML, `RepairHistory`, preferências ou base de conhecimento. A operação é exclusão lógica da linha SQLite, não sobrescrita segura do espaço livre. SQLite não fornece criptografia em repouso por si só; não armazenar senhas, tokens ou chaves.
