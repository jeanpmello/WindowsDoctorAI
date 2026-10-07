Attempting to perform the InitializeDefaultDrives operation on the 'FileSystem' provider failed.
# Contribuindo

Obrigado pelo interesse no Windows Doctor AI. O `main` contém a aplicação local de diagnóstico e os fluxos descritos no [README](README.md), na [SRS](docs/SRS.md), na [arquitetura](docs/Architecture.md) e no [roadmap](docs/Roadmap.md). Consulte também as [regras do projeto](PROJECT_RULES.md) e as diretrizes de [segurança](docs/Security.md). Código presente e CI verde não significam validação end-to-end ou compatibilidade de produção.

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

O build da solução WinUI 3/XAML e a validação das APIs nativas exigem Windows. Testes unitários e alguns testes de SQLite em memória podem rodar em outros sistemas quando os projetos/alvos permitirem, mas isso não comprova inicialização da UI nem funcionamento de WMI, Registro, Windows Update, Event Viewer, SMART ou Windows Server Backup em cada edição de Windows/hardware. Registre as verificações realmente executadas e suas limitações em qualquer proposta.

## Contribuições de código

- Escreva comentários e documentação em português brasileiro; mantenha nomes oficiais das tecnologias.
- Não adicione credenciais, segredos ou arquivos de máquina. Confirme `git diff --check` e `git status` antes de enviar.
- Diagnósticos são somente de leitura por padrão. Não envie dados do dispositivo a terceiros sem consentimento e documentação.
- Correções futuras devem ser ações catalogadas, com pré-condições, impacto e aprovação explícita por ação.

