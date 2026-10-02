# Arquitetura

**Estado:** solução do Milestone 1 implementada. O build e a execução da UI e da coleta WMI ainda devem ser validados em Windows; esta documentação não representa auditoria ou certificação.

## Visão geral

Windows Doctor AI é uma aplicação desktop WinUI 3, C#/.NET 9, organizada em Clean Architecture. O domínio descreve inventário e execução; contratos internos isolam scanner, preferências e histórico; o caso de uso coordena a coleta; adaptadores implementam WMI e SQLite; a aplicação WinUI compõe o grafo DI e apresenta os dados.

```text
WindowsDoctorAI.sln
├── src/
│   ├── WindowsDoctorAI.App/             # WinUI 3, MVVM, rotas, DI e logging
│   ├── WindowsDoctorAI.Core/            # Portas independentes dos adaptadores
│   ├── WindowsDoctorAI.Domain/          # Modelos e invariantes
│   ├── WindowsDoctorAI.Application/     # Caso de uso de diagnóstico
│   ├── WindowsDoctorAI.Infrastructure/ # Composição do adaptador SQLite
│   ├── WindowsDoctorAI.Diagnostics/    # Scanner e fonte WMI Windows
│   ├── WindowsDoctorAI.Repair/          # Contrato vazio de catálogo; sem executor
│   ├── WindowsDoctorAI.AI/              # Contrato opcional; sem provedor
│   ├── WindowsDoctorAI.Reporting/       # Formatador de inventário em texto
│   └── WindowsDoctorAI.Database/        # DbContext EF Core e repositórios SQLite
└── tests/WindowsDoctorAI.Tests/         # xUnit e testes SQLite em memória
```

## Direção de dependências

- `Domain` não depende de infraestrutura ou UI.
- `Core` referencia os tipos de domínio usados nas portas; não referencia UI, WMI, EF Core ou APIs externas.
- `Application` depende de `Core` e `Domain`, não de WinUI nem de banco concreto.
- `Diagnostics` implementa o contrato de scanner; a consulta ao WMI é um adaptador Windows substituível por fonte simulada.
- `Database` implementa os repositórios com EF Core/SQLite. `Infrastructure` expõe o registro das implementações para a composição.
- `App` é o composition root; configura Hosting, Configuration, Logging, DI, rotas e páginas WinUI.
- `AI`, `Repair` e `Reporting` permanecem separados. O milestone não registra serviço externo de IA nem executor de reparo.

## Fluxo de diagnóstico implementado

1. O usuário inicia a ação **Iniciar Diagnóstico**.
2. `RunComputerInventoryDiagnosticUseCase` solicita `IComputerInventoryScanner` e cria uma execução com score demonstrativo fixo em 95.
3. `ComputerInventoryScanner` delega a `IComputerInventoryDataSource`; no Windows, `WindowsManagementInventoryDataSource` usa WMI, APIs de rede, tipo de firmware e registro local para ler os campos suportados.
4. As informações são exibidas no dashboard. Se a opção de histórico estiver habilitada, a execução é serializada em JSON e gravada em `DiagnosticRuns`, no SQLite local.
5. Falhas ao persistir são reportadas sem descartar o resultado já coletado; falhas individuais WMI são registradas com o nome da consulta e deixam o campo correspondente indisponível.

A coleta não executa correções. Campos podem estar ausentes conforme firmware, hardware, versão do Windows e permissões. O score não é calculado a partir de achados e não deve ser interpretado como diagnóstico de saúde.

## Persistência e configuração

O arquivo do banco fica em `%LOCALAPPDATA%\WindowsDoctorAI\windowsdoctorai.db`. O schema inicial é criado com `EnsureCreated`; migrações versionadas ainda não existem. A configuração JSON e variáveis de ambiente `WINDOWSDOCTORAI_` são carregadas no App; logs são enviados ao provider Debug. O diretório e as tabelas estão descritos em [Database](Database.md).

## Limites e próximo trabalho

- Executar build, abrir as telas e validar consultas WMI em uma máquina Windows representativa.
- Criar migrações EF Core antes de evolução compatível do schema.
- Substituir score demonstrativo por metodologia explicável e versionada.
- Projetar regras de retenção e remoção do histórico; a opção atual apenas impede novas gravações.
- Definir matriz de Windows/hardware e avaliar o ciclo de suporte do .NET 9 antes de produção.
- Introduzir IA, plugins e correções somente após requisitos, consentimento e controles próprios.
