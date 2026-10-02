# Regras do projeto

Estas regras valem para código, documentação e contribuições do Windows Doctor AI.

## Arquitetura e dependências

- Preserve Clean Architecture e SOLID: dependências apontam para contratos estáveis; camadas centrais não referenciam WinUI, WMI, Entity Framework ou provedores externos.
- `Domain` mantém modelos/regras sem detalhes de I/O; `Core` mantém portas; `Application` coordena casos de uso; `Diagnostics`, `Database`, `AI` e `Repair` implementam integrações; `App` é a composição e apresentação.
- Evite referências circulares. Uma interface nova pertence à camada mais interna que consiga expressá-la sem conhecer detalhes da infraestrutura.
- Não execute correções automaticamente. A coleta é somente de leitura; qualquer reparo futuro exige ação catalogada e aprovação explícita.
- IA, integrações e rede ficam inativas até haver requisitos, configuração e consentimento próprios.

## C# e nomenclatura

- Use nullable reference types e implicit usings; não silencie warnings anuláveis sem justificativa.
- Tipos, métodos públicos e propriedades em `PascalCase`; campos privados em `_camelCase`; interfaces começam com `I`.
- Use `Async` no nome de métodos assíncronos e aceite `CancellationToken` em operações de I/O quando pertinente.
- Prefira modelos imutáveis, métodos pequenos e dependências injetadas por construtor.
- Documente APIs públicas e regras que não sejam óbvias. Mensagens ao usuário devem estar em português brasileiro.
- Logs nunca devem incluir segredos; minimize dados pessoais e identificadores de hardware.

## MVVM e interface

- A UI WinUI é declarativa em XAML; ViewModels usam CommunityToolkit.Mvvm e não acessam controles diretamente.
- Navegação e composição ficam em serviços da camada App. Views recebem ViewModels por DI.
- Estados indisponíveis ou erro de coleta não devem ser apresentados como sinal positivo de saúde.
- Inclua acessibilidade básica, seleção de texto para valores diagnósticos e estados de carregamento/erro.

## Persistência e privacidade

- Use SQLite local via EF Core; toda mudança de modelo deve considerar atualização do schema e testes.
- Não armazene senhas, tokens ou chaves em SQLite, configuração versionada ou logs.
- Documente dados pessoais/identificáveis gravados e ofereça controle explícito sobre sua retenção.
- Não marque dados como saudáveis quando uma leitura falha ou não é suportada.

## Testes e validação

- Testes automatizados ficam em `tests/WindowsDoctorAI.Tests` e usam xUnit.
- Inclua testes de unidade para regras e orquestração, e de integração em SQLite para repositórios.
- Isole WMI, Windows Registry, rede e outros efeitos de sistema atrás de contratos substituíveis; testes unitários não devem exigir hardware ou privilégios elevados.
- Antes de enviar, execute `dotnet test tests/WindowsDoctorAI.Tests/WindowsDoctorAI.Tests.csproj`, `dotnet build WindowsDoctorAI.sln` no Windows e `git diff --check`.
- Informe no PR todas as verificações executadas e as limitações do ambiente; não afirme teste Windows que não ocorreu.

## Commits e revisão

- Use Conventional Commits: `tipo(escopo): descrição` (por exemplo, `feat(diagnostics): add inventory check`).
- Pull requests devem explicar motivação, escopo, riscos, dados afetados, passos de validação e screenshots para mudanças de UI.
- Mantenha alterações focadas e documentação sincronizada com o comportamento real.
