# Especificação de Requisitos de Software (SRS)

**Produto:** Windows Doctor AI
**Versão deste documento:** 0.1 (baseline proposta)
**Estado:** requisitos iniciais; sujeitos a validação. O repositório ainda não contém uma implementação.

## 1. Introdução

O Windows Doctor AI é uma plataforma planejada para diagnosticar, analisar e apoiar a correção de problemas em Windows 10, Windows 11 e Windows Server. Este documento define uma baseline inicial para orientar a arquitetura e as entregas do roadmap; não certifica compatibilidade com versões específicas do Windows.

### 1.1 Objetivo

Organizar sinais de sistema e hardware em achados rastreáveis e compreensíveis, orientar a decisão de técnicos e equipes de infraestrutura e permitir correções controladas quando houver aprovação explícita.

### 1.2 Escopo

A proposta contempla uma aplicação Windows com interface WinUI 3, armazenamento local SQLite, mecanismos separados de diagnóstico e reparo, uma camada de apoio por IA opcional e pontos de extensão por plugins. A experiência inicial é local; gestão centralizada e outros recursos corporativos são evolução futura.

Fora do escopo desta baseline: execução remota de comandos em frota, monitoramento permanente, reparo autônomo sem aprovação, garantia de recuperação para qualquer falha, preços ou contratos comerciais e compatibilidade certificada com todas as edições/builds do Windows.

### 1.3 Termos

- **Achado (finding):** resultado estruturado de uma verificação, incluindo evidência e contexto.
- **Proposta de reparo:** ação possível associada a um achado; não equivale à execução.
- **Health Score:** resumo calculado por uma metodologia versionada, que deve poder ser explicada.
- **Diagnóstico:** execução identificável de verificações, com início, fim e resultados.

## 2. Usuários e partes interessadas

- **Técnico de suporte:** investiga um dispositivo e avalia recomendações para atender incidentes.
- **Administrador de TI/infraestrutura:** define procedimentos e precisa de resultados consistentes e auditáveis.
- **Responsável de segurança ou privacidade:** avalia privilégios, coleta, retenção e integrações externas.
- **Usuário do dispositivo:** inicia um diagnóstico, compreende a coleta e decide se aprova uma correção.

Uma pessoa pode exercer mais de um desses papéis. Permissões organizacionais específicas ainda precisam ser detalhadas.

## 3. Requisitos funcionais

Os requisitos abaixo usam “deverá” como requisito proposto para versões futuras.

### 3.1 Diagnóstico e resultados

- **FR-01 — Início informado:** o sistema deverá permitir iniciar um diagnóstico sob demanda e apresentar seu escopo antes da coleta.
- **FR-02 — Coleta mínima:** cada scanner deverá declarar os dados que lê, finalidade, privilégios necessários e limitações; a coleta deverá se limitar ao necessário para as verificações habilitadas.
- **FR-03 — Resultados rastreáveis:** cada execução deverá identificar versões da aplicação, dos scanners e da metodologia de Health Score usada.
- **FR-04 — Achados estruturados:** cada achado deverá conter categoria, gravidade, horário, descrição, evidência disponível, origem da verificação e recomendação; a confiança só deverá ser apresentada quando houver método definido para calculá-la.
- **FR-05 — Explicabilidade do score:** o Health Score deverá mostrar fatores contribuintes e versão da fórmula, sem substituir detalhes dos achados nem ser apresentado como diagnóstico médico/garantia de saúde.
- **FR-06 — Histórico:** o usuário deverá poder consultar execuções e resultados armazenados localmente, bem como remover o histórico conforme política de retenção definida.
- **FR-07 — Relatórios:** quando implementada a exportação, o usuário deverá revisar o conteúdo e os identificadores incluídos antes de compartilhar o relatório.

### 3.2 Correções

- **FR-08 — Separação entre sugerir e executar:** recomendações e propostas deverão ser distinguíveis de ações executadas.
- **FR-09 — Aprovação por ação:** antes de uma correção, a interface deverá informar efeito esperado, escopo, impacto conhecido, privilégios necessários e possibilidade/limites de reversão, e solicitar aprovação explícita para aquela ação.
- **FR-10 — Validação de pré-condições:** o mecanismo de reparo deverá confirmar pré-condições antes de alterar o sistema e interromper a ação se não puder fazê-lo com segurança.
- **FR-11 — Registro do resultado:** cada reparo tentado deverá ter estado e resultado registráveis; falhas parciais não poderão ser reportadas como sucesso.
- **FR-12 — Reversão:** quando tecnicamente possível, cada ação deverá documentar a estratégia de restauração; a interface não deverá prometer reversão quando ela não estiver disponível.

### 3.3 Dados e IA

- **FR-13 — Persistência local:** execuções, achados e histórico deverão ser armazenados em SQLite local na primeira fase.
- **FR-14 — IA opcional:** análises por IA deverão permanecer desligadas por padrão até o usuário ou administrador configurá-las e aprovar o envio dos dados pertinentes.
- **FR-15 — IA não executora:** uma saída de IA poderá auxiliar a explicação ou priorização, mas não deverá iniciar uma correção por conta própria.
- **FR-16 — Plugins controlados:** verificações adicionais deverão usar interfaces e políticas documentadas; carregar extensão não confiável ou executar código arbitrário não deverá ser comportamento padrão.

## 4. Requisitos não funcionais

- **NFR-01 — Segurança:** aplicar privilégio mínimo, validar entradas, proteger a integridade dos resultados e exigir consentimento para alterações no sistema.
- **NFR-02 — Privacidade:** minimizar a coleta, informar sua finalidade, não inserir segredos em logs e permitir que o núcleo de diagnóstico funcione sem enviar dados a terceiros.
- **NFR-03 — Confiabilidade:** uma falha de scanner não deverá corromper os resultados já concluídos nem ser confundida com um resultado limpo.
- **NFR-04 — Desempenho:** o tempo e o uso de recursos deverão ser medidos em hardware representativo; metas numéricas serão definidas depois de protótipos e testes.
- **NFR-05 — Compatibilidade:** definir e testar uma matriz de Windows por release; “Windows 10”, “Windows 11” e “Windows Server” neste documento são famílias pretendidas, não cobertura irrestrita.
- **NFR-06 — Acessibilidade e usabilidade:** apresentar gravidade, evidência e ações com linguagem clara, navegação acessível e distinção visual entre leitura, recomendação e mudança aplicada.
- **NFR-07 — Manutenibilidade:** manter domínios e dependências separados e versionar esquemas de dados, regras de diagnóstico e métodos de pontuação.
- **NFR-08 — Observabilidade:** registros técnicos deverão ser úteis para suporte, minimizados e submetidos a controles contra exposição de dados pessoais ou segredos.

## 5. Evolução futura

1. **Alpha 0.1:** fundação, Core Engine, persistência SQLite e dashboard inicial.
2. **Alpha 0.2:** scanners do Windows e hardware, resultados estruturados e Health Score.
3. **Beta:** catálogo de correções assistidas, relatórios e base de conhecimento.
4. **1.0:** apoio de IA e recursos corporativos.

Critérios de aceitação, matriz de sistemas suportados, perfis de privilégio, políticas de retenção, orçamento de desempenho e especificação da integração de IA deverão ser acordados antes das respectivas implementações. Veja [Roadmap](Roadmap.md) e [Architecture](Architecture.md).
