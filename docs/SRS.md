Attempting to perform the InitializeDefaultDrives operation on the 'FileSystem' provider failed.
# Especificação de Requisitos de Software (SRS)

**Produto:** Windows Doctor AI Core
**Versão:** 0.2
**Estado:** baseline de requisitos alinhada ao `main` em 7 de outubro de 2026. Este documento descreve escopo e limites conhecidos; não certifica compatibilidade, segurança ou prontidão para produção.

## 1. Objetivo e público

O Windows Doctor AI Core é um aplicativo desktop local para técnicos que precisam coletar sinais de um computador Windows, revisar achados e consultar orientações explicáveis. A coleta disponível é de leitura. O produto não deve sugerir que uma associação entre sinais comprova causa nem que um score representa a saúde total do computador.

## 2. Escopo implementado no `main`

- Aplicativo Windows em WinUI 3/.NET 9 com MVVM e camadas separadas.
- Inventário local, configurações e persistência SQLite. Gravação de histórico de diagnóstico é opt-in e começa desligada.
- Scanners somente de leitura para Windows Update, Services, Drivers, Disk, Event Viewer e metadados de Windows Server Backup. Falhas e fontes indisponíveis são estados explícitos, não resultados saudáveis.
- Health Score heurístico calculado apenas quando há verificações confirmadas; ele não mede saúde global.
- Importação deliberada de pacotes JSON de conhecimento com validação e prévia, recomendações explicáveis e correlações observacionais.
- Exportação local de relatório HTML da execução atual ou mais recente.
- Framework de reparo com confirmação e auditoria, sem plugin de alteração do Windows registrado na composição atual.

Consulte [Arquitetura](Architecture.md), [Roadmap](Roadmap.md), [Segurança](Security.md) e [Banco de dados](Database.md) para contratos, limites e detalhes. A [Alpha](alpha-windows-11-x64.md) descreve a distribuição atualmente documentada.

## 3. Requisitos funcionais

### Diagnóstico

1. O aplicativo deve apresentar o resultado e o estado de cada scanner, distinguindo achado, sucesso confirmado, falha e indisponibilidade.
2. Scanners devem operar em modo de leitura e isolar falhas para que uma fonte indisponível não seja tratada como saudável.
3. O Health Score deve ser identificado como heurístico e não deve ser apresentado quando não houver verificações confirmadas.
4. Correlações devem apontar os identificadores e resultados que as sustentam e não devem afirmar causa determinada ou ordem temporal sem evidência estruturada suficiente.

### Conhecimento e orientação

1. A Knowledge Base deve começar vazia; importar um pacote requer seleção e revisão explícitas da pessoa usuária.
2. A prévia deve validar schema e limites antes de persistir. Falhas ou conflitos não podem deixar importação parcial.
3. Regras e referências importadas devem ser apresentadas como conteúdo declarado. Hash de conteúdo não deve ser descrito como prova de autoria ou veracidade.
4. Recomendações devem expor a correspondência e suas evidências, distinguindo força do match de probabilidade de causa ou sucesso.
5. Orientações que possam modificar o computador devem permanecer manuais até que uma ação específica, suas pré-condições, riscos, consentimento e validação sejam implementados e revisados.

### Histórico, relatórios e privacidade

1. O histórico de diagnóstico deve permanecer local e opt-in, com novas gravações redigidas.
2. Retenção por idade e exclusão integral do histórico devem exigir confirmação explícita.
3. Relatórios HTML devem codificar texto não confiável, evitar recursos remotos e ser salvos somente por ação deliberada.
4. O aplicativo deve informar que relatórios exportados ficam fora da retenção do banco e que SQLite não fornece, por si só, criptografia em repouso ou apagamento físico seguro.
5. Dados do dispositivo não devem ser enviados a serviços externos no escopo atual do `main`.

### Segurança de reparo

1. Nenhum reparo pode ocorrer automaticamente.
2. O engine deve exigir consentimento vinculado à ação/plano e registrar a tentativa antes de chamar um plugin.
3. Rollback, quando suportado, deve exigir consentimento próprio e referenciar uma execução original concluída com sucesso.
4. Enquanto não houver verificadores de estado implementados, pré/pós-condições devem ser tratadas como declarações não verificadas.

## 4. Fora do escopo atual

- Provedor de IA ativo no `main`. A proposta de integração com Ollama está em PR aberto e não faz parte do comportamento integrado.
- Plugin que modifique o Windows, restauração real de backup ou execução de comandos de reparo.
- PDF gerado pelo aplicativo, timeline confiável, comparação de diagnósticos ou determinação causal.
- Agente, servidor, identidade, autorização, isolamento multi-tenant, sincronização ou dashboard Enterprise.
- Assinatura/autenticação criptográfica de pacotes de conhecimento.

Esses itens só passam a ser requisitos de uma entrega quando tiverem escopo, critérios de aceite e revisão de segurança definidos.

## 5. Critérios de validação e limitações conhecidas

- A CI Windows compila a solução e executa testes automatizados. Isso não substitui execução visual da UI nem validação das APIs nativas em máquinas reais.
- Scanners WSB usam runner falso e fixtures sintéticas nos testes; `Get-WBBackupSet` e os tipos runtime ainda precisam de smoke test em Windows Server 2019.
- Compatibilidade declarada para a alpha é Windows 11 x64. O README informa que a distribuição é unpackaged, sem assinatura e sem instalador.
- Testes em hardware, versões Windows, permissões e configurações representativas devem ser registrados separadamente dos testes unitários e de CI.

## 6. Evolução dos requisitos

Ao adicionar um scanner, integração externa ou ação de reparo, atualizar esta SRS e os documentos relacionados para registrar dados lidos/enviados/alterados, finalidade, consentimento, privilégios, falhas, retenção e evidência de validação. O roadmap define o que é implementação atual, validação pendente ou visão futura.

