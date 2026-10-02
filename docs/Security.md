# Segurança

**Estado:** controles e testes unitários descritos abaixo; não houve auditoria, certificação ou validação end-to-end em Windows. Os scanners continuam somente de leitura. Nenhum plugin de reparo que altere o sistema está registrado.

## Controles atuais

- O manifesto permanece `asInvoker`; a aplicação não pede elevação administrativa.
- WMI, Registro e APIs Windows são acessados por adaptadores de leitura substituíveis.
- Knowledge JSON é dado declarativo, não código. Schemas 1.0 (compatibilidade) e 1.1 (condição estrita) são limitados a 512 KiB/500 regras, rejeitam propriedades não mapeadas, limitam listas/texto, aceitam enum textual e referências HTTPS, e recusam caminhos absolutos, controles, scripts e padrões de comando/injeção. No 1.1, um código exato só casa quando scanner indicado e todos os termos de contexto estão no mesmo achado; condições genéricas e listas legadas de sintomas/códigos são recusadas.
- Na tela inicial, a seleção de um arquivo JSON mostra versão, fonte declarada, SHA-256 e total de regras após validar schema/limites e antes de persistir. A Knowledge Base começa vazia; estado vazio é apresentado explicitamente. Pacotes inválidos ou conflitantes são recusados sem gravação parcial.
- Nenhuma DLL/script/plugin é descoberto a partir do JSON ou carregado de diretório. A origem textual e SHA-256 são metadados; SHA-256 não autentica o fornecedor. As URLs não são buscadas nem verificadas pelo app.
- `knowledge-packs/microsoft-windows-update-pilot.json` é uma curadoria de primeira parte a partir de duas páginas Microsoft. URLs e publisher declarado não equivalem a assinatura/autenticidade criptográfica; a UI continua sinalizando autoria não verificada. O arquivo é apenas disponibilizado para seleção: não é pré-carregado nem semeado no banco.
- As recomendações só existem quando há regra importada e match literal em achado coletado. A confiança informa força do match, não probabilidade de causa/sucesso; a fonte da regra continua não verificada automaticamente.
- Correlação compartilha identificadores observados e nunca declara causa determinada. Saúde/causa não é inferida a partir de indisponibilidade.
- Cada tentativa de reparo tem `RepairExecutionId` e registro local antes de chamar o plugin. O consentimento imutável identifica RepairId, versão, risco, alvo, ação e fingerprint do plano; confirmação booleana legada não autoriza ação. Recusas também ficam registradas. O plugin precisa aguardar o marco de início auditado antes de efeitos; falha ou cancelamento após o marco fica explícito como potencialmente parcial/inconclusivo. Se a gravação prévia falhar, o plugin não é chamado.
- Rollback requer o ID da execução original concluída com sucesso e consentimento separado para aquele rollback. A tentativa de rollback tem seu próprio ID e guarda o vínculo à origem; falhas e cancelamentos são auditados. A API legada de rollback sem ID não chama plugin.
- Pré/pós-condições e alvo no plano são metadados declarativos. O engine não consulta o estado do Windows e registra `NotEvaluated` quando não há verificador; um retorno bem-sucedido do plugin, sozinho, não comprova que o estado desejado foi alcançado. Exceções são registradas sem a mensagem interna, para reduzir exposição de dados.
- A aplicação não registra plugins executáveis de sistema, não invoca shell/processos, não pede elevação e não altera serviços, Registro, arquivos, updates ou drivers. O plugin de demonstração/teste é inerte; os fakes determinísticos não simulam estado nem logs do Windows.
- As duas orientações do piloto são manuais e exigem confirmação. `0xC1900107` recomenda reiniciar e repetir Setup; nada reinicia o computador automaticamente. `0x80073712` recomenda DISM elevado e modificador e, somente após sucesso do DISM, SFC; em falha de DISM, preservar/inspecionar CBS.log. Não há plugin real, execução de correção nem rollback documentado para essa sequência.
- HTML de relatório codifica conteúdo textual não confiável e não carrega recursos ativos de origem remota. Links HTTPS são referências declaradas e só abrem por ação externa do usuário.
- Histórico pode ser desativado para diagnósticos; logs não devem receber segredos nem payload integral desnecessário.

## Revisão e confiança dos pacotes

O schema evita campos para comandos, caminhos ou executáveis e rejeita campos desconhecidos. O conteúdo não é interpretado como código nem executa comandos; textos com padrões comuns de comando/injeção são recusados. Isso reduz superfície de execução, mas não torna verdadeiras as afirmações importadas: um arquivo ainda pode conter conteúdo enganoso, referências sem relação ou impacto incorreto. A prévia e o hash não comprovam autoria, assinatura, reputação ou veracidade. Revise origem, versão, regras e URLs antes da importação. A validação do domínio HTTPS não verifica autenticidade, disponibilidade ou conteúdo da página. Para o piloto, o termo “curadoria de primeira parte” descreve a seleção editorial das páginas Microsoft citadas, não uma atestação criptográfica da origem do arquivo.

A interface permite salvar o relatório HTML no destino escolhido pelo usuário, a partir da execução atual ou mais recente carregada. O relatório inclui os resultados e evidências existentes, recomendações que correspondam a regras importadas e até 100 registros recentes de propostas de reparo. O HTML escapa conteúdo textual e não carrega scripts, estilos ou imagens remotos; referências HTTPS são links declarados, não conteúdo incorporado. `High` representa força de correspondência literal, nunca probabilidade causal ou de sucesso. Revise o conteúdo local — que pode conter identificadores do equipamento — antes de compartilhar.

## Dados locais

Com histórico ligado, SQLite grava resultados que podem conter identificadores do equipamento/usuário, rede e mensagens de eventos. Relatórios HTML repetem parte dessas evidências. Dados não são enviados a serviços externos neste milestone. SQLite não fornece criptografia em repouso. A opção de desligar histórico não remove registros existentes.

## Próximos controles antes de reparos reais ou Enterprise

- Para cada reparo real: pré-condições e verificadores efetivos de pós-condições, alvo/alcance, privilégios mínimos, risco, plano de restauração quando viável, aprovação separada e vinculada por ação, tratamento de interrupção/parcialidade e testes em Windows isolado. Os contratos desta fatia não suprem essas validações.
- Para importar conhecimento: assinatura/proveniência verificável, processo de revisão e critérios de confiança/impacto antes de distribuir pacotes oficiais; a prévia e o hash atuais não autenticam autoria.
- Para relatórios: minimização, seleção de destino, retenção e compartilhamento deliberado.
- Para Enterprise: autenticação, autorização, isolamento de tenants, transporte, inventário central, retenção, auditoria, resposta a incidentes e consentimento devem ter desenho próprio. Nada disso é fornecido pelo Core atual.
- Revisar dependências, threat model e APIs Windows antes de releases. Testar contas padrão e hardware/firmware distintos.
