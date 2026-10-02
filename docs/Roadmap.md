# Roadmap

Este roadmap distingue código implementado de validações ainda pendentes. Compatibilidade e critérios devem ser revistos a cada etapa.

## Milestone 1 — Fundação e inventário (código implementado)

**Entregue no repositório**

- Solution .NET 9 e projetos em Clean Architecture.
- Dashboard WinUI 3 com navegação Inicial/Sobre/Configurações, MVVM Toolkit, Dependency Injection, Logging e Configuration.
- Scanner de inventário local; modelo inclui sistema/hardware, BIOS, UEFI, TPM, Secure Boot, usuário/domínio, rede e uptime.
- Persistência local SQLite opcional, repositórios EF Core e testes unitários/de SQLite.
- IA e reparos sem provedor ou executor ativado.

Registros históricos deste milestone podem conter score demonstrativo fixo em 95. O dashboard novo não o apresenta como saúde calculada.

## Milestone 2 — Diagnostic Engine (código implementado)

**Entregue no repositório**

- Modelo compartilhado `DiagnosticResult`, porta `IDiagnosticScanner`, `IDiagnosticEngine` e resumo consolidado.
- Plugins registrados para Windows Update, Services, Drivers, Disk e Event Viewer; execução concorrente apenas quando permitida, duração medida e falhas isoladas.
- Score heurístico baseado em achados observados; indisponibilidade e dados não verificados não são considerados saudáveis.
- Dashboard com score calculado/não calculado, problemas críticos, avisos, duração, última execução, cobertura por categoria e evidências.
- Formatador textual do relatório e persistência do resumo junto ao inventário sem alterar o schema SQLite.
- Consultas locais somente de leitura. Sem reparos, Knowledge Base ou geração de achados/evidências simulados em produção.

**Validações que faltam antes de considerar o milestone aceito em runtime**

- Compilar a solution e validar XAML/WinUI com Windows App SDK em Windows.
- Exercitar Windows Update Agent, Registro, WMI, Event Viewer e permissões em versões/edições Windows representativas.
- Confirmar SMART, saúde e temperaturas em hardware/controladoras diferentes, e ajustar limiares/lista de serviços conforme evidência.
- Revisar visualmente o dashboard e validar a execução/persistência do diagnóstico no Windows.

Os testes de unidade usam fontes falsas em memória e cobrem lógica, falhas e estados indisponíveis; não demonstram que APIs Windows ou hardware real foram exercitados.

## Próximas etapas — base de conhecimento e qualidade

- Projetar uma Knowledge Base versionada e explicável, com fontes, limites de confiança e sem executar reparos.
- Validar falsos positivos e critérios de gravidade com logs e hardware reais, sem gravar evidências de teste como achados de produção.
- Criar migrações EF Core antes de mudanças futuras no schema; o Milestone 2 mantém o schema e evolui o JSON da execução.
- Formalizar retenção, revisão e remoção do histórico e minimização de dados pessoais em relatórios.

## Etapas futuras — plugins, reparos assistidos e IA

- Adicionar plugins confiáveis para SQL Server, Exchange, VMware, Docker, Microsoft 365, Proxmox ou outros domínios, com permissões próprias.
- Propor reparos catalogados apenas após requisitos próprios e aprovação explícita por ação; nenhuma execução automática.
- Tornar análise por IA opcional, com consentimento, minimização de dados e saída não executável.
- Definir identidade, acesso, auditoria, implantação, retenção e requisitos organizacionais antes de gestão em escala.
