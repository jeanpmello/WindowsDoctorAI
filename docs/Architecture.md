# Arquitetura

**Estado:** Milestones 1 e 2 têm implementação no repositório. A compilação e a execução da UI e das consultas dependentes do Windows ainda precisam ser validadas em Windows; este documento não representa certificação.

## Visão geral

Windows Doctor AI é uma aplicação desktop WinUI 3, C#/.NET 9, organizada em Clean Architecture. O domínio define inventário e resultados diagnósticos comuns; `Core` contém portas; `Application` orquestra o inventário e o Diagnostic Engine; `Diagnostics` implementa os plugins locais de leitura; `Reporting` formata relatórios; e a aplicação WinUI compõe dependências e apresenta o estado.

```text
WindowsDoctorAI.sln
├── src/
│   ├── WindowsDoctorAI.App/             # WinUI 3, MVVM, composição e dashboard
│   ├── WindowsDoctorAI.Core/            # IDiagnosticScanner, IDiagnosticEngine e portas
│   ├── WindowsDoctorAI.Domain/          # Inventário, DiagnosticResult e DiagnosticReport
│   ├── WindowsDoctorAI.Application/     # DiagnosticEngine e caso de uso coordenador
│   ├── WindowsDoctorAI.Diagnostics/    # Plugins e adaptador local de APIs Windows
│   ├── WindowsDoctorAI.Reporting/      # Formatadores de relatório em texto
│   ├── WindowsDoctorAI.Database/       # DbContext EF Core e repositórios SQLite
│   ├── WindowsDoctorAI.Infrastructure/ # Composição dos adaptadores SQLite
│   ├── WindowsDoctorAI.AI/             # Integração opcional, sem provedor ativo
│   └── WindowsDoctorAI.Repair/          # Catálogo sem executor de reparos
└── tests/WindowsDoctorAI.Tests/         # xUnit e testes SQLite em memória
```

## Contrato e execução

`DiagnosticResult` fornece o formato compartilhado `ScannerName`, `Category`, `Severity`, `Status`, `Title`, `Description`, `Recommendation`, `Evidence`, `Duration` e `Timestamp`. `IDiagnosticScanner` é o contrato dos plugins, e `IDiagnosticEngine` coordena todos os scanners registrados no contêiner de dependências.

O engine inicia em paralelo os plugins que declaram `SupportsParallelExecution`; os que não declaram segurança concorrente executam sequencialmente. Uma falha de plugin produz um resultado `Unavailable`, é registrada e não interrompe os demais. O engine normaliza nome/categoria, substitui duração e horário pelos valores medidos e consolida resultados em `DiagnosticReport`.

Estados `Unavailable` e `NotVerified` não são tratados como saúde nem entram no Health Score. O score desta etapa usa a regra explícita **100 − 25 por achado crítico − 8 por aviso**, limitada a 0–100; só é exibido se ao menos uma verificação tiver sido confirmada. É uma métrica heurística desta etapa, não uma garantia de saúde global. A cobertura por categoria indica verificações incompletas. Os scanners atuais cobrem Sistema, Drivers e Hardware; Rede e Segurança aparecem como não verificadas, pois não há plugins desses domínios neste milestone.

## Plugins incluídos e limites da coleta

Os plugins são registrados em `AddWindowsDiagnosticPlugins` no assembly de diagnósticos. O conjunto atual é: Windows Update (busca do Windows Update Agent, marcadores de reinicialização e eventos recentes); Services (serviços centrais, inicialização, estados automáticos parados e dependências); Drivers (códigos de configuração Plug and Play, inclusive código 28); Disk (SMART, status geral de disco, espaço livre e temperatura SMART quando a interface expõe o atributo); e Event Viewer (logs System, Application e Windows Update).

As consultas dependem de APIs e componentes locais do Windows: Windows Update Agent/COM e Registro, WMI (`Win32_Service`, `Win32_PnPEntity`, classes de disco/SMART) e Windows Event Log. Falta de permissão, classe ausente, controladora ou hardware sem suporte resulta em `Unavailable` ou `NotVerified`; não é convertido em resultado saudável. O caminho de temperatura usa atributos SMART ATA 190/194 quando legíveis e não aplica um limite universal por modelo.

Para limitar o volume, a análise do Event Viewer lê eventos Critical, Error e Warning dos três logs nos últimos sete dias, até 100 registros recentes por log. O Windows Update procura eventos recentes no log operacional dos últimos 14 dias, até 100 registros. Espaço livre abaixo de 10% gera achado crítico e abaixo de 20% gera aviso; são limiares operacionais gerais. Serviços parados por gatilho, dispositivos desativados/não conectados e estados sem classificação não são automaticamente tratados como falhas.

A coleta é local e somente de leitura: não instala atualizações ou drivers, não inicia/para serviços, não modifica o Registro nem apaga arquivos. Recomendações descrevem revisão manual e não são executadas.

## Extensão segura

Um plugin implementa `IDiagnosticScanner`, retorna a lista comum de `DiagnosticResult` e declara se pode executar em paralelo. Os módulos registrados pelo agregador `AddWindowsDiagnosticPlugins` são resolvidos automaticamente pelo engine via `IEnumerable<IDiagnosticScanner>`. Um novo domínio pode ser implementado em outro módulo/projeto e ligado ao agregador de plugins sem modificar `Core`, o `DiagnosticEngine`, o caso de uso ou a implementação do app; o app mantém uma única chamada ao registro do conjunto de diagnósticos.

Essa extensão permite planejar módulos independentes para SQL Server, Exchange, VMware, Docker, Microsoft 365 ou Proxmox, com contratos de dados e permissões próprios. Esta etapa **não** carrega DLLs arbitrárias de diretórios: plugins são código confiável explicitamente incluído e registrado, não executáveis descobertos automaticamente em disco.

## Dashboard, relatório e histórico

O caso de uso executa o scanner de inventário existente e o Diagnostic Engine, preserva o inventário e agrega o `DiagnosticReport` na execução. O dashboard exibe score calculado ou não calculado, achados críticos, avisos, duração, última execução, cobertura e evidências. A tela limita os achados mostrados a dez; o formatador `DiagnosticReportFormatter` mantém os resultados completos para geração de texto.

O SQLite continua sem mudança de schema: o JSON da execução agora inclui o relatório. Registros antigos do Milestone 1, que não contêm `Report`, continuam úteis para o inventário; o dashboard informa que não há score diagnóstico calculável, sem reaproveitar o antigo valor demonstrativo como saúde real.

## Limites e validação pendente

- Compilar a solution e validar XAML/WinUI com Windows App SDK em Windows.
- Exercitar WUA, Registro, WMI, Event Viewer, SMART e permissões em diferentes versões/edições e hardware Windows.
- Medir falsos positivos dos limites gerais de espaço, lista de serviços centrais e telemetria SMART em máquinas representativas.
- Testar exportação/retensão de relatórios e histórico com política de privacidade explícita.
- Knowledge Base, análise por IA e reparos continuam fora deste milestone.
