# Arquitetura

**Estado:** proposta de arquitetura. Esta descrição não indica que projetos, interfaces ou módulos já estejam implementados.

## Visão geral

Windows Doctor AI é planejado como uma aplicação local Windows construída com C#, .NET 9, WinUI 3 e SQLite, organizada segundo Clean Architecture. O desenho separa interface, regras de domínio e casos de uso, mecanismos do sistema operacional e persistência. As dependências devem apontar para abstrações estáveis do Core; regras centrais não devem depender de WinUI, SQLite ou provedores de IA.

### Estrutura de diretórios proposta

A estrutura a seguir registra os diretórios/projetos previstos. `tests/` é apenas o diretório raiz de testes planejado; os projetos de teste ainda não foram especificados.

```text
WindowsDoctorAI/
├── src/
│   ├── WindowsDoctorAI.App/
│   ├── WindowsDoctorAI.Core/
│   ├── WindowsDoctorAI.Diagnostics/
│   ├── WindowsDoctorAI.Repair/
│   ├── WindowsDoctorAI.AI/
│   └── WindowsDoctorAI.Database/
└── tests/
```

## Camadas e módulos

### `WindowsDoctorAI.App` — apresentação e composição

Aplicação WinUI 3 planejada: dashboard, fluxo de execução de diagnósticos, visualização de resultados, solicitação de consentimento para reparos e composição de dependências. A camada de apresentação não deverá conter regras de diagnóstico nem executar comandos diretamente.

### `WindowsDoctorAI.Core` — domínio e casos de uso

Núcleo de regras e contratos: conceitos de diagnóstico, achado, severidade, proposta de reparo, autorização, interfaces de scanner/repositório e coordenação dos casos de uso. Deve permanecer independente das tecnologias de UI e de armazenamento.

### `WindowsDoctorAI.Diagnostics` — Diagnostic Engine

Implementações de verificações locais para Windows e hardware, chamadas por casos de uso do Core. Cada verificação deverá declarar escopo, evidência, requisitos de privilégio e erros possíveis. Uma leitura indisponível ou uma falha de scanner não pode ser interpretada como ausência de problema.

### `WindowsDoctorAI.Repair` — Repair Engine

Catálogo e execução de correções aprovadas. Deve validar pré-condições, explicar o efeito, usar apenas o privilégio necessário, registrar o resultado e oferecer reversão quando ela for tecnicamente suportada. O módulo não deve receber instruções executáveis livres geradas por IA.

### `WindowsDoctorAI.AI` — AI Engine

Abstração de provedores e integração opcional para resumir ou contextualizar evidências. Deve aceitar dados minimizados, tratar saídas como conteúdo não confiável e retornar recomendações estruturadas. Não deve poder invocar o Repair Engine sem passar pelo fluxo de consentimento definido no Core.

### `WindowsDoctorAI.Database` — persistência

Implementação SQLite de repositórios e migrações para execuções, achados, propostas e histórico. O Core define os contratos; o banco não define regras de domínio. Consulte [Database](Database.md).

## Direção de dependências

- `WindowsDoctorAI.Core` não depende dos demais módulos de produto.
- `WindowsDoctorAI.App`, `Diagnostics`, `Repair`, `AI` e `Database` podem depender de contratos do Core.
- A aplicação compõe implementações concretas; o Core coordena operações por interfaces.
- Chamadas entre diagnóstico, IA, banco e reparo devem passar por casos de uso/contratos, e não por dependências circulares.
- Detalhes de Windows e SQLite ficam fora do domínio central.

Esta orientação deverá ser refinada ao definir a solução .NET; não antecipa projetos auxiliares ou subprojetos de teste.

## Fluxos principais propostos

### Diagnóstico

1. A aplicação explica e inicia um caso de uso do Core.
2. O Core solicita verificações habilitadas aos scanners de Diagnostics.
3. Os resultados são normalizados em achados; falhas e limitações permanecem explícitas.
4. O Database persiste execução e achados, e a aplicação apresenta evidências e score versionado.
5. Se houver uso de IA configurado, o fluxo solicita consentimento e envia somente dados necessários ao AI Engine; a resposta é exibida como apoio, não como comando.

### Reparo

1. A aplicação apresenta uma proposta e seus efeitos conhecidos.
2. O Core verifica aprovação explícita, pré-condições e privilégios.
3. O Repair Engine executa a ação catalogada; o Database registra estados e resultado.
4. A aplicação informa sucesso, falha parcial ou reversão disponível sem transformar falha em êxito.

## Plugin System

A arquitetura prevê extensão controlada de verificações e conteúdo. A interface de plugin, manifesto, origem confiável, compatibilidade, atualização e isolamento ainda precisam ser especificados. A primeira versão não deverá carregar código arbitrário só porque está em um diretório de plugins. Plugins de reparo exigem controles de confiança e o mesmo fluxo de aprovação das ações nativas.

## Tecnologias e decisão pendente

A baseline recebida para documentação é C#, .NET 9, WinUI 3, SQLite e Clean Architecture. A [política oficial de suporte do .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), atualizada em 8 de setembro de 2026, informa que o .NET 9 entra em fim de suporte em **10 de novembro de 2026**. Portanto, a versão exata do framework deve ser reavaliada e confirmada antes de criar a solução e antes de qualquer lançamento; este registro preserva a intenção atual sem declarar .NET 9 como alvo seguro de longo prazo.
