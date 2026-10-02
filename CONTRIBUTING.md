# Contribuindo

Obrigado pelo interesse no Windows Doctor AI. O repositório agora contém a solution inicial do Milestone 1, além da documentação do produto. Consulte o [README](README.md), as [regras do projeto](PROJECT_RULES.md), os requisitos em [docs/SRS.md](docs/SRS.md), a arquitetura em [docs/Architecture.md](docs/Architecture.md), o [roadmap](docs/Roadmap.md) e as diretrizes de [segurança](docs/Security.md).

## Antes de propor uma mudança

- Identifique se a alteração afeta domínio, contratos, interface, persistência, privacidade ou comportamento do scanner.
- Não descreva como disponível uma funcionalidade que esteja apenas planejada.
- Para verificações/reparos, documente dados lidos ou alterados, finalidade, erros possíveis e requisitos de privilégio.
- Para uma vulnerabilidade, siga [docs/Security.md](docs/Security.md); não publique detalhes exploráveis em uma issue pública.

## Fluxo de trabalho

1. Crie uma branch de trabalho a partir de `main`.
2. Mantenha a alteração focada e respeite a direção das dependências em `PROJECT_RULES.md`.
3. Inclua testes para regras e casos de falha, atualizando a documentação relacionada.
4. Abra um pull request com motivação, escopo, dados afetados e validações realizadas; inclua screenshots quando mudar a UI.
5. Aguarde revisão antes de integrar as alterações.

## Desenvolver, compilar e testar

Use Windows com .NET 9 SDK para compilar e executar a interface WinUI 3. Na raiz do repositório:

```powershell
dotnet restore .\WindowsDoctorAI.sln
dotnet build .\WindowsDoctorAI.sln --configuration Release
dotnet test .\tests\WindowsDoctorAI.Tests\WindowsDoctorAI.Tests.csproj --configuration Release
dotnet run --project .\src\WindowsDoctorAI.App\WindowsDoctorAI.App.csproj
```

Os testes unitários e de SQLite em memória podem rodar em Linux, mas o compilador XAML do Windows App SDK exige Windows. O teste unitário com fonte simulada não comprova que todas as consultas WMI funcionem em cada edição de Windows/hardware. Registre essas limitações em qualquer proposta.

## Contribuições de código

- Escreva comentários e documentação em português brasileiro; mantenha nomes oficiais das tecnologias.
- Não adicione credenciais, segredos ou arquivos de máquina. Confirme `git diff --check` e `git status` antes de enviar.
- Diagnósticos são somente de leitura por padrão. Não envie dados do dispositivo a terceiros sem consentimento e documentação.
- Correções futuras devem ser ações catalogadas, com pré-condições, impacto e aprovação explícita por ação.
