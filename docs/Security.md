# Segurança

**Estado:** princípios aplicados no Milestone 1, sem auditoria, certificação ou validação end-to-end em Windows. Esta entrega é somente de leitura: não há executor de reparos, comandos gerados, chamadas remotas de IA ou envio automático de inventário.

## Controles presentes

- O manifesto executa como `asInvoker`; a aplicação não pede elevação administrativa.
- O scanner separa WMI, registro e APIs locais atrás de `IComputerInventoryDataSource` e faz apenas consultas/leitura.
- Falhas WMI individuais são registradas sem converter a leitura ausente em resultado saudável.
- A integração com IA é apenas uma interface, sem provedor registrado; o módulo de reparos expõe somente um catálogo vazio.
- O histórico SQLite pode ser desligado nas Configurações. Logs não devem incluir payload integral do inventário ou segredos.

## Dados locais

Com histórico habilitado, SQLite grava o inventário completo da execução, incluindo possíveis identificadores do computador, usuário e endereços de rede. O arquivo fica no perfil local do usuário em `%LOCALAPPDATA%\WindowsDoctorAI\windowsdoctorai.db`. A opção de desligar histórico bloqueia novas gravações, mas não remove registros anteriores. SQLite não fornece criptografia em repouso por si só.

## Requisitos para evolução

- Continuar separando recomendação de execução. Qualquer reparo futuro precisa ser catalogado, informar efeito/impacto/privilégio, validar pré-condições e pedir aprovação explícita por ação.
- Manter IA e demais conexões externas opt-in, com categorias de dados exibidas e minimização antes de transmissão.
- Avaliar retenção, exclusão, proteção do banco, tratamento de erros de hardware e superfícies WMI/Registro antes de distribuição.
- Executar testes de integração no Windows em contas padrão e hardware/firmware distintos; revisar dependências e threat model antes de releases.

Não há promessa de compatibilidade com todas as versões do Windows, nem declaração de que os controles foram submetidos a avaliação de segurança independente.
