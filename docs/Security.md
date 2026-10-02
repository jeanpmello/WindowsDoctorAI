# Segurança

**Estado:** controles e testes unitários descritos abaixo; não houve auditoria, certificação ou validação end-to-end em Windows. Os scanners continuam somente de leitura. Nenhum plugin de reparo que altere o sistema está registrado.

## Controles atuais

- O manifesto permanece `asInvoker`; a aplicação não pede elevação administrativa.
- WMI, Registro e APIs Windows são acessados por adaptadores de leitura substituíveis.
- Knowledge JSON é dado declarativo, não código. O importador limita o pacote a 512 KiB/500 regras, rejeita propriedades não mapeadas, limita listas e texto, aceita somente enum textual e referências HTTPS, e rejeita caminhos absolutos Windows/POSIX, controles e padrões comuns de comando/injeção.
- Nenhuma DLL/script/plugin é descoberto a partir do JSON ou carregado de diretório. A origem textual e SHA-256 são metadados; SHA-256 não autentica o fornecedor. As URLs não são buscadas nem verificadas pelo app.
- As recomendações só existem quando há regra importada e match literal em achado coletado. A confiança informa força do match, não probabilidade de causa/sucesso; a fonte da regra continua não verificada automaticamente.
- Correlação compartilha identificadores observados e nunca declara causa determinada. Saúde/causa não é inferida a partir de indisponibilidade.
- Reparos exigem confirmação explícita no framework e geram auditoria. A aplicação não registra plugins executáveis de sistema, não invoca shell/processos e não altera serviços, Registro, arquivos, updates ou drivers. O plugin de demonstração/teste é inerte.
- HTML de relatório codifica conteúdo textual não confiável e não carrega recursos ativos de origem remota. Links HTTPS são referências declaradas e só abrem por ação externa do usuário.
- Histórico pode ser desativado para diagnósticos; logs não devem receber segredos nem payload integral desnecessário.

## Importação confiável

O schema evita campos para comandos, caminhos ou executáveis e rejeita campos desconhecidos. Isso reduz superfície de execução, mas não torna verdadeiras as afirmações importadas: um arquivo ainda pode conter conteúdo enganoso, referências sem relação ou impacto incorreto. Revise origem, versão, regras e URLs antes da importação. A validação do domínio HTTPS não verifica autenticidade, reputação, disponibilidade ou conteúdo da página.

## Dados locais

Com histórico ligado, SQLite grava resultados que podem conter identificadores do equipamento/usuário, rede e mensagens de eventos. Relatórios HTML repetem parte dessas evidências. Dados não são enviados a serviços externos neste milestone. SQLite não fornece criptografia em repouso. A opção de desligar histórico não remove registros existentes.

## Próximos controles antes de reparos reais ou Enterprise

- Para cada reparo real: pré-condições, alcance, privilégios mínimos, risco, plano de restauração quando viável, aprovação separada por ação, resultado auditável e testes em Windows isolado.
- Para importar conhecimento: assinatura/proveniência verificável, processo de revisão e critérios de confiança/impacto antes de distribuir pacotes oficiais.
- Para relatórios: minimização, seleção de destino, retenção e compartilhamento deliberado.
- Para Enterprise: autenticação, autorização, isolamento de tenants, transporte, inventário central, retenção, auditoria, resposta a incidentes e consentimento devem ter desenho próprio. Nada disso é fornecido pelo Core atual.
- Revisar dependências, threat model e APIs Windows antes de releases. Testar contas padrão e hardware/firmware distintos.
