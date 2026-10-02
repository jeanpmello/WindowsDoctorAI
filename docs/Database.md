# Banco de dados

**Estado:** persistência inicial implementada com EF Core 9 e SQLite. O schema pode evoluir; a aplicação ainda não dispõe de migrações versionadas, ferramenta de remoção/exportação de histórico ou criptografia própria.

## Localização e inicialização

A aplicação cria `windowsdoctorai.db` em `%LOCALAPPDATA%\WindowsDoctorAI\` e inicializa o schema com `EnsureCreated`. No Linux, os testes usam SQLite em memória; isso não altera o comportamento nem o local do banco Windows.

## Estrutura inicial

- **`DiagnosticRuns`** — chave `Id`, timestamp de conclusão em Unix milliseconds para ordenar o resultado mais recente, e `PayloadJson` que contém a execução e o inventário tipado.
- **`UserSettings`** — chave fixa `Id = 1` e `SaveDiagnosticHistory`, inicializada como `true`.

`SqliteDiagnosticRunRepository` serializa/deserializa os modelos com `System.Text.Json`; `SqliteUserSettingsRepository` carrega/salva a preferência. O caso de uso consulta a configuração a cada execução e só persiste quando o histórico está habilitado. Falha de persistência não descarta o inventário já apresentado e gera aviso técnico no app.

## Dados e privacidade

O payload pode incluir nome e fabricante/modelo/série do computador, Windows/build, CPU/RAM/GPU, modelos e capacidade de discos físicos, volumes/BIOS, estado de TPM e Secure Boot, usuário/domínio, endereços IPv4/IPv6 e adaptadores. São dados locais, mas alguns identificam usuário ou dispositivo. A preferência pode impedir novas gravações em **Configurações**; desligá-la não apaga execuções existentes. Exclusão do histórico ainda não está disponível.

SQLite por si só **não garante criptografia em repouso**. Não armazenar senhas, tokens ou chaves no banco ou nos logs. Dados de diagnóstico não são enviados a serviços externos pelo Milestone 1.

## Limites e evolução

`EnsureCreated` é adequado apenas ao schema inicial; alterações futuras requerem migrações EF Core sequenciais, testadas com bancos existentes antes da publicação. Também devem ser definidos retenção, remoção, backup/recuperação, comportamento multiusuário e eventual proteção em repouso antes de ampliar o histórico.
