# Configuração

A aplicação carrega `src/WindowsDoctorAI.App/appsettings.json`, substituível no diretório de saída, e variáveis de ambiente com o prefixo `WINDOWSDOCTORAI_`. Os logs usam o provider Debug do Microsoft.Extensions.Logging.

As preferências de usuário editáveis no Milestone 1 são persistidas na tabela `UserSettings` do banco SQLite local. A opção `SaveDiagnosticHistory` controla se novas execuções são gravadas; não remove dados existentes.

## IA local opcional

`Ai:Ollama` configura o provedor local. O pacote atual define `Enabled: true`, `BaseUrl: http://127.0.0.1:11434` e `Model: llama3.1:8b`. O provedor recusa endereços fora do loopback. Para usar o assistente, instale e inicie Ollama, baixe o modelo (`ollama pull llama3.1:8b`) e use **Testar IA sem dados do computador** na Home. O teste envia somente uma solicitação sintética ao modelo local. A análise de um diagnóstico só ocorre quando a pessoa revisa o prompt exibido e seleciona **Analisar diagnóstico**.

O teste de conectividade e geração não salva o texto enviado nem a resposta. A aplicação não controla o que o processo local Ollama armazena. Identificadores conhecidos são redigidos antes da montagem do prompt, sem garantia de anonimização completa. Não configure um endpoint de rede: endereços não locais são recusados.

A localização e os limites do arquivo estão em [Database.md](Database.md). Esta versão não oferece uma tela para editar níveis de log ou parâmetros WMI.
