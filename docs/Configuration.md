# Configuração

A aplicação carrega `src/WindowsDoctorAI.App/appsettings.json`, substituível no diretório de saída, e variáveis de ambiente com o prefixo `WINDOWSDOCTORAI_`. Os logs usam o provider Debug do Microsoft.Extensions.Logging.

As preferências de usuário editáveis no Milestone 1 são persistidas na tabela `UserSettings` do banco SQLite local. A opção `SaveDiagnosticHistory` controla se novas execuções são gravadas; não remove dados existentes. Não há chaves, senhas ou provedores externos configurados por padrão.

A localização e os limites do arquivo estão em [Database.md](Database.md). Esta versão não oferece uma tela para editar níveis de log ou parâmetros WMI.
