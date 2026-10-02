# Roadmap

Este roadmap é uma sequência proposta de entregas, não um calendário nem uma declaração de funcionalidades disponíveis. Escopo, critérios e compatibilidade devem ser revistos a cada etapa. O repositório está atualmente em fase documental.

## Alpha 0.1 — Fundação

**Escopo planejado**

- Criar a solução e a estrutura de módulos descrita em [Architecture](Architecture.md).
- Implementar o primeiro Core Engine: composição e fluxo básico de casos de uso.
- Preparar persistência SQLite, esquema inicial e migrações.
- Criar um dashboard inicial para apresentar estado e navegação.
- Definir logs técnicos mínimos, configurações não secretas e estratégia de testes.

**Critérios para encerrar a etapa**

- A aplicação inicia e apresenta o dashboard em um ambiente Windows validado.
- Migrações podem criar e atualizar o banco sem perder dados de teste.
- Os limites entre apresentação, Core e persistência podem ser verificados por testes.
- Nenhuma correção automática é executada nesta etapa.
- O alvo .NET e a matriz inicial de sistemas operacionais foram confirmados antes do build de release.

## Alpha 0.2 — Scanner e Health Score

**Escopo planejado**

- Implementar scanners para sinais selecionados do Windows e do hardware.
- Normalizar resultados em achados rastreáveis e persistir execuções.
- Calcular Health Score com método versionado e explicação dos fatores.
- Apresentar gravidade, evidência, limitações e recomendações no dashboard.

**Critérios para encerrar a etapa**

- Falhas, falta de permissão e verificações não aplicáveis são distintas de resultado saudável.
- Os dados coletados e privilégios requeridos estão documentados por verificação.
- A metodologia do score pode ser explicada e testada com resultados reproduzíveis.
- Diagnósticos são somente de leitura, exceto se uma ação separada e explicitamente aprovada vier a ser definida.

## Beta — Correções, relatórios e conhecimento

**Escopo planejado**

- Introduzir catálogo de correções automáticas controladas pelo Repair Engine.
- Gerar relatórios revisáveis antes da exportação.
- Criar base de conhecimento versionada para causas, evidências e recomendações.
- Registrar aprovação, execução, resultado e possibilidade/limites de reversão.

**Critérios para encerrar a etapa**

- Cada ação informa efeito esperado, impacto, privilégio e estratégia de restauração conhecida.
- É exigida confirmação explícita por ação; recomendações não disparam execução.
- Pré-condições, cancelamento e falhas parciais são cobertos por testes.
- Relatórios permitem revisar dados identificáveis antes do compartilhamento.

## 1.0 — IA e recursos corporativos

**Escopo planejado**

- Adicionar análise assistida por IA, com configuração transparente, consentimento e minimização de dados.
- Desenvolver recursos organizacionais como políticas e gestão em escala, conforme requisitos de empresas validados.
- Formalizar segurança, atualização, suporte de versões Windows e documentação operacional.

**Critérios para encerrar a etapa**

- O uso de IA é opcional e suas respostas não executam reparos.
- A organização pode entender que dados entram em cada fluxo externo e controlar a integração.
- Funcionalidades empresariais têm requisitos de identidade, acesso, auditoria, implantação e retenção definidos antes da entrega.
- Os processos de instalação, atualização, desinstalação e resposta a vulnerabilidades foram testados.

## Dependências e decisões abertas

- Confirmar a versão suportada do .NET antes do início da Alpha 0.1; .NET 9 tem fim de suporte previsto para 10/11/2026 segundo a [política da Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
- Definir a matriz de versões/edições do Windows, arquitetura e hardware mínimo.
- Priorizar verificações e correções com técnicos e equipes de TI.
- Definir políticas de retenção, exportação, IA, plugins e recursos corporativos antes das etapas correspondentes.
