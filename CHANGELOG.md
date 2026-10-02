# Changelog

Mudanças relevantes do Windows Doctor AI.

## [Unreleased]

### Added

- Solution `WindowsDoctorAI.sln` em .NET 9 com os dez projetos de `src/` e `tests/WindowsDoctorAI.Tests`.
- App WinUI 3 com navegação Inicial/Sobre/Configurações, MVVM Toolkit, DI, Logging e Configuration.
- Caso de uso de diagnóstico e scanner `ComputerInventoryScanner` com adaptador Windows somente de leitura para WMI, BIOS/firmware, TPM, Secure Boot e interfaces de rede.
- Histórico e preferências locais com EF Core/SQLite; retenção futura pode ser desligada nas Configurações.
- Cobertura xUnit para score, scanner, cancelamento, caso de uso, persistência opcional e round-trip SQLite.
- README, `PROJECT_RULES.md` e documentação de arquitetura, persistência e segurança revisados.

### Notes

- Health Score permanece fixo em 95 para demonstração.
- Testes de UI, compilador XAML e coleta física dependem de Windows; não executados neste ambiente Linux.
- .NET 9 foi mantido conforme escopo solicitado; avaliar o fim de suporte previsto em 10/11/2026 antes de produção.
