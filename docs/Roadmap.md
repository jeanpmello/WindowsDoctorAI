# Roadmap

Este roadmap distingue o código entregue das verificações e funcionalidades ainda planejadas. Compatibilidade e critérios devem ser revistos a cada etapa.

## Milestone 1 — Fundação e inventário (código implementado)

**Entregue no repositório**

- Solution .NET 9 e projetos em Clean Architecture.
- Dashboard WinUI 3 com navegação Inicial/Sobre/Configurações, MVVM Toolkit, Dependency Injection, Logging e Configuration.
- Health Score fixo em 95, identificado como demonstrativo.
- Scanner de inventário local; modelo inclui sistema/hardware, BIOS, UEFI, TPM, Secure Boot, usuário/domínio, rede e uptime.
- Persistência local SQLite opcional, repositórios EF Core e testes unitários/de SQLite.
- IA e reparos sem provedor ou executor ativado.

**Validações que faltam antes de considerar o milestone aceito em runtime**

- Compilar a solution e executar a UI num ambiente Windows com o SDK e Windows App SDK suportados.
- Exercitar navegação, carregamento do histórico e fluxo de diagnóstico em Windows.
- Validar as consultas WMI e o estado de firmware/TPM/Secure Boot em hardware/edições representativos.
- Confirmar a matriz de Windows e rever o suporte do .NET 9 antes de qualquer release.

## Próxima etapa — Resultados rastreáveis e score real

- Definir verificações e achados estruturados com gravidade, evidência, fonte e tratamento de falhas.
- Criar score com método e versão documentados, sem sinalizar leitura indisponível como saudável.
- Acrescentar migrações EF Core e testes de compatibilidade de schema.
- Permitir revisar/excluir o histórico e formalizar a política de retenção.

## Etapas futuras — reparos assistidos, relatórios e conhecimento

- Propor reparos catalogados, mas exigir consentimento por ação e verificação de pré-condições.
- Expandir relatórios com revisão/minimização dos dados identificáveis antes de exportação.
- Definir base de conhecimento e extensões confiáveis com controles de atualização e segurança.

## Etapas futuras — IA e uso organizacional

- Tornar análise por IA opcional, com consentimento, minimização de dados e saída não executável.
- Definir identidade, acesso, auditoria, implantação, retenção e requisitos de clientes corporativos antes de qualquer gestão em escala.
