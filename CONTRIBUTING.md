# Contribuindo

Obrigado pelo interesse no Windows Doctor AI. O projeto está em fase de planejamento: o repositório contém documentação, mas ainda não possui solução .NET, código de aplicação ou pipeline de CI. Não presuma comandos de build, testes ou convenções que ainda não foram definidos.

## Antes de propor uma mudança

- Consulte o [README](README.md), a [especificação de requisitos](docs/SRS.md), a [arquitetura proposta](docs/Architecture.md), o [roadmap](docs/Roadmap.md) e as diretrizes de [segurança](docs/Security.md).
- Para mudanças de escopo ou arquitetura, explique o problema, quem é afetado e as alternativas consideradas.
- Não descreva como disponível uma funcionalidade que esteja apenas planejada.
- Para uma vulnerabilidade, siga o procedimento indicado em [docs/Security.md](docs/Security.md); não publique detalhes exploráveis em uma issue pública.

## Fluxo de trabalho

1. Crie uma branch de trabalho a partir de `main`.
2. Mantenha a alteração pequena e focada; não inclua arquivos fora do escopo relacionado.
3. Abra um pull request com contexto, motivação, mudanças e validações realizadas. Para documentação, indique as páginas afetadas e confira os links relativos.
4. Aguarde revisão antes de integrar alterações à branch principal.

## Contribuições de documentação

- Escreva em português brasileiro, use Markdown e preserve os nomes oficiais de tecnologias e módulos.
- Diferencie explicitamente requisitos, propostas e funcionalidades implementadas.
- Prefira linguagem verificável: evite prometer compatibilidade, segurança, desempenho ou recursos corporativos ainda não validados.
- Atualize os documentos relacionados quando uma decisão alterar requisitos, arquitetura, segurança, dados ou roadmap.
- Verifique `git diff --check`, a árvore de arquivos e os links relativos antes de enviar.

## Contribuições de código futuras

Quando a solução de código for criada, este guia deverá ser atualizado com pré-requisitos e comandos reais de instalação, build, testes e análise estática. Novas verificações de diagnóstico e reparo devem incluir testes apropriados, explicar seus efeitos e respeitar o princípio de privilégio mínimo. Nenhuma contribuição deve executar reparos silenciosamente ou enviar dados do dispositivo a serviços externos sem consentimento e documentação.
