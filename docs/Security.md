# Segurança

**Estado:** requisitos e controles propostos. Este documento não representa auditoria, certificação ou garantia de segurança; a aplicação ainda não foi implementada.

## Princípios

1. **Privilégio mínimo:** diagnóstico deverá funcionar sem elevação sempre que possível. A elevação será solicitada apenas para uma operação que a exija, com finalidade explícita.
2. **Leitura antes de alteração:** verificações são somente de leitura por padrão. Diagnóstico e proposta de reparo são estados diferentes.
3. **Consentimento informado:** antes de cada correção, mostrar ação, efeito esperado, escopo, impacto conhecido, privilégios e possibilidade de reversão; exigir confirmação explícita para aquela ação.
4. **Segurança por falha:** diante de pré-condição desconhecida, integridade não verificada ou permissão insuficiente, não aplicar a correção.
5. **Privacidade por padrão:** minimizar dados e manter fluxos locais sem envio externo por padrão.
6. **Auditoria sem excesso:** registrar decisões e resultados necessários sem capturar senhas, tokens ou conteúdo pessoal desnecessário.

## Controle de privilégios

- O App deverá operar com a conta e as permissões normais do usuário quando possível.
- Elevação administrativa deverá usar mecanismos normais do Windows e informar por que é necessária.
- Não armazenar credenciais administrativas nem contornar prompts do sistema operacional.
- Uma ação elevada deverá ser limitada à operação aprovada; resultados de scanners não devem ser convertidos em comandos arbitrários.
- A política de acesso para implantação corporativa, serviço Windows e execução remota permanece fora da baseline e requer revisão de ameaça própria.

## Aprovação e reversão de correções

O Repair Engine deverá trabalhar com ações catalogadas e validadas, não com comandos livres gerados por IA. A tela de confirmação deverá permitir cancelar e distinguir proposta, aprovação, execução, falha parcial, êxito e reversão.

Antes de alterar o dispositivo, o produto deverá validar as pré-condições e, quando aplicável e suportado pelo sistema, criar ou verificar uma forma de recuperação. A documentação da ação deverá declarar limites e casos em que a reversão não é possível. Não se deve alegar que toda operação é recuperável.

## Logs e histórico

- Armazenar horários em UTC, identificadores de execução, versão do scanner e resultado necessário para auditoria.
- Evitar registrar segredos, linhas de comando com credenciais, conteúdo integral de arquivos pessoais e identificadores desnecessários.
- Sanitizar dados de erro vindos do Windows, plugins e provedores externos.
- Restringir arquivos ao usuário/instalação conforme modelo escolhido; proteção em repouso, compartilhamento multiusuário, retenção e exportação precisam de decisão e teste específicos.
- Permitir localizar o fluxo que originou uma alteração, sem registrar mais dados do que o necessário.

## IA e integrações externas

IA é planejada como recurso opcional. Antes de uma solicitação externa, a interface deverá indicar provedor e categorias de dados transmitidas, obter aprovação quando requerido e reduzir ou remover identificadores desnecessários. Não enviar telemetria ou diagnósticos para terceiros por padrão.

Conteúdo de diagnóstico, conhecimento e plugins deve ser tratado como entrada não confiável. A saída de IA deverá ser validada como dado, não interpretada como código; recomendações não têm permissão para chamar ferramentas de reparo, executar scripts ou alterar o dispositivo.

## Plugins e cadeia de atualização

O formato de plugin, assinatura, política de confiança, permissões e isolamento ainda não estão definidos. Antes de habilitar execução de plugins, elaborar threat model, validar origem e integridade, limitar capacidades e documentar atualização e revogação. Downloads e atualizações do produto deverão ter integridade/autenticidade verificáveis antes de distribuição.

## Relato responsável de vulnerabilidades

Não há, nesta fase documental, endereço dedicado ou SLA de resposta publicados. Se encontrar uma vulnerabilidade, evite divulgar publicamente detalhes exploráveis e contate o mantenedor por um canal privado disponível no repositório; um canal formal deverá ser definido antes da distribuição do software.

## Revisão antes de lançamento

Antes de Beta e 1.0, revisar threat model, permissões por scanner/reparo, dependências, atualização, armazenamento, exportações, provedores de IA e plugins. Realizar testes de abuso e falha, revisão de código e validação em sistemas suportados; registrar riscos residuais e procedimentos de resposta.
