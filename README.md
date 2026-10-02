# Windows Doctor AI

**Windows Doctor AI** é a proposta de uma plataforma de diagnóstico, análise e correção assistida para computadores Windows, voltada a técnicos, empresas e equipes de infraestrutura.

> **Estado do repositório:** planejamento e documentação. O produto ainda não tem código funcional; funcionalidades, arquitetura e etapas abaixo são propostas, não capacidades já entregues.

## Objetivo

Ajudar equipes de suporte a identificar problemas de saúde do Windows, entender evidências e escolher correções com segurança. O produto deve transformar sinais dispersos do sistema em diagnósticos compreensíveis e rastreáveis, sem deixar que recomendações automatizadas alterem o computador sem autorização humana.

## Sistemas operacionais planejados

- Windows 10
- Windows 11
- Windows Server

A lista expressa o escopo pretendido, não uma garantia de compatibilidade. Edições, versões, builds, arquiteturas, requisitos mínimos e ciclos de suporte deverão ser especificados e testados antes de cada versão.

## Funcionalidades planejadas

- **Diagnóstico local:** coleta controlada de informações de saúde do sistema e do hardware.
- **Análise explicável:** achados com evidências, gravidade, nível de confiança e recomendações compreensíveis.
- **Health Score:** visão resumida, acompanhada da metodologia e dos fatores que influenciam a pontuação.
- **Correções assistidas:** catálogo de ações, avaliação de impacto, consentimento por ação e registro do resultado.
- **Histórico e relatórios:** consulta a diagnósticos anteriores e exportação com minimização de dados identificáveis.
- **Base de conhecimento e plugins:** extensão de verificações e orientações sob controles de confiança.
- **IA opcional:** apoio à interpretação de resultados, sujeito a configuração, minimização de dados e aprovação do usuário.

## Tecnologias planejadas

- C# e .NET 9
- WinUI 3
- SQLite para armazenamento local
- Clean Architecture
- Módulos de diagnóstico, reparo, IA e plugins

**Atenção ao ciclo de suporte:** a [política oficial da Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) indica fim de suporte do .NET 9 em **10 de novembro de 2026**. Como essa data está próxima, a versão-alvo deve ser confirmada antes da implementação e de qualquer lançamento; este documento registra .NET 9 como tecnologia planejada, não como decisão imutável para produção.

## Roadmap

- **Alpha 0.1:** fundação da solução, Core Engine, SQLite e dashboard inicial.
- **Alpha 0.2:** scanner do Windows e do hardware, com Health Score.
- **Beta:** correções automáticas sob aprovação, relatórios e base de conhecimento.
- **1.0:** assistência de IA e recursos para empresas.

Consulte [docs/Roadmap.md](docs/Roadmap.md) para escopo e critérios propostos por etapa.

## Modelo freemium — proposta

A hipótese de produto prevê um núcleo gratuito para uso individual e recursos pagos destinados a empresas, como administração em escala, políticas centralizadas, integrações e suporte organizacional. Preços, limites, licenciamento, tratamento de dados e disponibilidade dos planos **não estão definidos**; esta descrição não é uma oferta comercial.

## Segurança e privacidade

A ferramenta deverá privilegiar diagnósticos somente de leitura e executar correções apenas após consentimento explícito, apresentando o efeito esperado e registrando o resultado. Elevação de privilégios deve ser pontual e justificada. O uso de IA deve ser opcional, com divulgação do que será enviado e sem transmissão de dados por padrão. Consulte [docs/Security.md](docs/Security.md) e [docs/Database.md](docs/Database.md).

## Contribuição

Leia [CONTRIBUTING.md](CONTRIBUTING.md) antes de propor alterações. Requisitos e decisões de projeto estão em [`docs/`](docs/).

## Licença

Este projeto está sob a licença MIT; consulte [LICENSE](LICENSE).
