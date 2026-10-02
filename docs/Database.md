# Banco de dados

**Estado:** desenho inicial proposto. O repositório ainda não possui schema, migrações ou código de persistência.

## Objetivos e limites

A primeira fase prevê SQLite local para armazenar configurações não secretas, execuções de diagnóstico, achados, propostas de reparo, resultados e conteúdo de conhecimento. O modelo deve facilitar histórico e auditoria sem guardar dados além do necessário. Recursos corporativos centralizados, sincronização e replicação não fazem parte deste schema inicial.

Como proposta, o banco poderá residir em uma pasta de dados da aplicação no perfil local do usuário; o caminho definitivo, comportamento em dispositivos multiusuário e política de backup deverão ser validados antes da implementação. Nenhuma credencial, senha, token ou chave de API deve ser armazenada em tabelas de negócio.

## Tabelas previstas

Os nomes abaixo são conceituais e podem ser refinados quando o esquema for implementado.

- **`SchemaMigrations`** — versão da migração, data UTC e checksum para manter a evolução do schema rastreável.
- **`DeviceProfiles`** — identificador local e atributos mínimos necessários para contextualizar resultados, como família/build do Windows e arquitetura; evitar inventário excessivo.
- **`DiagnosticRuns`** — dispositivo, horários de início/fim, estado, versão da aplicação/scanners e versão da metodologia do Health Score.
- **`Findings`** — execução associada, categoria, severidade, origem da verificação, descrição, evidência minimizada, recomendação, horário e confiança apenas quando calculável.
- **`RepairProposals`** — achado de origem, chave da ação catalogada, parâmetros permitidos e avaliação de impacto; nunca guardar script arbitrário para execução.
- **`RepairExecutions`** — proposta, decisão/horário de aprovação, início/fim, estado, resultado e referência limitada a detalhes técnicos necessários.
- **`KnowledgeBaseEntries`** — identificador, versão, tópico, texto revisado, fonte, data de revisão e estado de publicação.
- **`AIAnalyses`** *(opcional)* — provedor/modelo, horário, consentimento, impressão digital da entrada minimizada e resultado necessário; não persistir prompt bruto por padrão.
- **`AuditEvents`** — eventos de segurança e mudanças relevantes com horário, tipo, resultado e identificador de correlação, sem segredos.
- **`AppSettings`** — preferências não secretas necessárias à aplicação; tokens de provedores deverão usar armazenamento apropriado do sistema operacional, fora desta tabela.

## Relações e integridade

Uma execução pertence a um perfil local de dispositivo e pode ter muitos achados. Um achado pode originar nenhuma ou várias propostas, e uma proposta pode ter tentativas de execução registradas. Eventos de auditoria podem referenciar a execução ou reparo por identificadores de correlação, mantendo o detalhe limitado. Entradas de conhecimento são versionadas para que mudanças de conteúdo não alterem retrospectivamente a explicação de resultados antigos.

As chaves estrangeiras deverão estar habilitadas e as exclusões em cascata definidas conscientemente. Remover um histórico deverá também aplicar a regra documentada aos achados e referências derivadas; registros exigidos para auditoria não devem ser retidos indefinidamente sem finalidade e política explícitas.

## Consultas e índices iniciais

Índices candidatos incluem horário/estado de `DiagnosticRuns`, `Findings` por execução e severidade, `RepairExecutions` por proposta/estado e entradas de conhecimento por tópico/versão. A seleção definitiva deverá ser orientada por consultas reais e medidas, evitando indexação de dados identificáveis sem necessidade.

Campos de tempo deverão ser normalizados em UTC. Campos estruturados flexíveis, se usados em JSON, precisam de validação, versão e limites de tamanho. O formato de evidência deverá separar dados essenciais do texto de apresentação para permitir migração e redação.

## Migrações, consistência e recuperação

- Criar migrações sequenciais, transacionais e verificáveis; não depender de alteração manual do arquivo SQLite.
- Registrar versão e checksum da migração aplicada.
- Ativar integridade referencial e documentar estratégia de concorrência; validar comportamento de bloqueio/backup em Windows.
- Uma escrita incompleta não pode aparecer como execução concluída; falhas devem persistir estado apropriado sem mascarar resultados anteriores.
- Testar atualização entre versões, recuperação de banco interrompido, exportação/importação se definida e remoção conforme retenção.
- Criptografia em repouso não é garantida pelo uso de SQLite por si só. A necessidade e o mecanismo de proteção deverão ser avaliados antes de armazenar dados sensíveis.

## Retenção e privacidade

O histórico deve ter política de retenção configurável ou claramente documentada. Relatórios e análises externas devem minimizar nomes de usuário, nomes de máquina, identificadores de hardware e outros dados que não sejam necessários para o propósito escolhido. A exclusão e a exportação devem ser testadas antes de serem anunciadas como disponíveis.
