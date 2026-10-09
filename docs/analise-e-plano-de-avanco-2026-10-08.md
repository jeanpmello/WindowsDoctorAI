# Análise e plano de avanço — Windows Doctor AI

**Data:** 8 de outubro de 2026  
**Branch analisada:** `main`  
**Repositório:** `jeanpmello/WindowsDoctorAI`

## 1. Resumo executivo

O projeto já possui uma base técnica consistente para uma **Alpha de diagnóstico local do Windows**:

- solução .NET 9 com WinUI 3/MVVM e separação em camadas;
- scanners locais de leitura para Windows Update, Services, Drivers, Disk, Event Viewer e metadados de Windows Server Backup;
- SQLite opt-in, redação de dados, relatórios HTML locais e importação controlada de conhecimento;
- recomendações explicáveis, correlação conservadora e framework de reparo sem plugin real registrado;
- testes automatizados relevantes e CI Windows verde;
- pacote Alpha self-contained para Windows 11 x64 com smoke test de XAML.

O principal gargalo não é a ausência de arquitetura. É a distância entre **“compila e passa nos testes”** e **“funciona de forma comprovada em Windows real, com UX validada e documentação coerente”**.

### Veredito

O próximo objetivo deve ser um **Milestone 3.1 — Alpha operacional validada**, antes de Enterprise, reparos reais ou aumento expressivo de escopo.

## 2. O que foi verificado

- Repositório clonado na branch `main`, sem alterações locais iniciais.
- CI Windows recente com execuções concluídas com sucesso.
- Nenhuma issue aberta foi encontrada no GitHub.
- O sandbox Linux não possui o SDK `dotnet`; portanto, não foi possível executar `dotnet test` localmente.
- O CI usa `msbuild`/`dotnet test` em `windows-latest` e inclui um smoke test do XAML no pacote Alpha.
- A solução contém aproximadamente 25 arquivos de teste C# e cerca de 19 mil linhas somando `src` e `tests`.

## 3. Pontos fortes

### Arquitetura

A solução mantém uma fronteira clara entre domínio, contratos, aplicação, adaptadores Windows, banco, UI, reparo, relatório e IA. Isso facilita testes substituíveis e reduz acoplamento com WMI, WinUI, SQLite e Ollama.

### Segurança e contenção

A decisão de manter os scanners somente de leitura é correta para a Alpha. Também são positivos:

- falha fechada para respostas inválidas do catálogo WSB;
- ausência de execução de comandos vindos de JSON;
- consentimento tipado e auditado para futuros reparos;
- redação antes de persistir/exportar;
- histórico desligado por padrão;
- IA restrita a Ollama em loopback;
- aviso explícito de que score, correlação e confiança não são causalidade ou garantia de sucesso.

### Qualidade de engenharia

Há testes para regras, matching, privacidade, migrações SQLite, cancelamento, composição, relatórios HTML, navegação e contratos de WSB simulados. O workflow também valida o conteúdo do pacote, arquitetura PE x64, ausência de padrões óbvios de segredo e inicialização XAML.

## 4. Riscos e lacunas prioritárias

### P0 — Documentação divergente do código

O código e o README já incorporam a análise opcional via Ollama, mas `docs/SRS.md` ainda afirma que:

- o provedor de IA não está ativo no `main`;
- a integração está em PR aberto;
- a IA está fora do escopo atual.

Isso pode causar decisões erradas, testes incompletos e comunicação incorreta com usuários. A SRS, o Roadmap, o README e o CHANGELOG precisam ser alinhados em uma única mudança documental.

### P0 — Validação em Windows real

Ainda falta comprovar, em hosts controlados:

- execução visual completa da UI;
- WUA/Windows Update;
- WMI e inventário;
- Registro, Event Viewer, drivers e permissões;
- comportamento em hardware representativo;
- catálogo `Get-WBBackupSet` e tipos runtime no Windows Server 2019;
- funcionamento com e sem permissões administrativas;
- Ollama ausente, modelo ausente, timeout, cancelamento e resposta inválida.

A CI atual é forte para compilação, testes e smoke de XAML, mas não é uma matriz E2E do Windows.

### P1 — Responsividade e densidade da interface

A UI apresenta alguns riscos claros para janelas estreitas:

- `MainWindow.xaml` fixa uma coluna lateral de 220 px;
- `HomePage.xaml` usa grids fixos de três e duas colunas;
- há muitos painéis longos em uma única página;
- listas de recomendações e evidências podem gerar rolagem excessiva;
- vários botões ficam em `StackPanel` horizontal e podem não acomodar largura reduzida.

O layout deve ser testado no mínimo em uma janela estreita, uma janela desktop padrão e uma janela maximizada. O objetivo não precisa ser “mobile nativo”, mas deve haver reflow funcional, foco de teclado, leitura clara e nenhuma sobreposição/corte.

### P1 — UX operacional

A tela inicial concentra diagnóstico, CBS, backup, conhecimento, recomendações, reparo, inventário e IA. Isso é funcional, mas aumenta carga cognitiva. Recomenda-se separar o fluxo em etapas ou seções recolhíveis:

1. executar diagnóstico;
2. revisar resumo e score;
3. explorar achados;
4. consultar orientações;
5. exportar relatório;
6. ações avançadas, como CBS, catálogo WSB e IA.

Também vale substituir textos longos sempre visíveis por `InfoBar`, `Expander`, estados vazios e detalhes sob demanda.

### P1 — Release e distribuição

A Alpha é unpackaged, não assinada e depende do VC++ Redistributable. Isso é aceitável para uma Alpha técnica, mas deve haver uma decisão explícita para a próxima distribuição:

- continuar ZIP interno;
- publicar Release do GitHub com checksum;
- assinar o executável;
- criar instalador/MSIX;
- manter o escopo estritamente Windows 11 x64.

Não é recomendável declarar suporte amplo antes da matriz de validação.

### P2 — Ciclo de vida tecnológico

A documentação registra .NET 9 com suporte indicado até novembro de 2026. Como o projeto está em outubro de 2026, é necessário abrir uma decisão técnica sobre migrar para a versão suportada seguinte ou congelar a base para a Alpha, documentando prazo e impacto.

### P2 — Proveniência do conhecimento

O hash SHA-256 identifica bytes, mas não comprova autoria. Para pacotes oficiais, o próximo passo deve ser uma cadeia de confiança verificável, por exemplo assinatura de pacote e chave pública embutida/configurada de forma segura. Até lá, manter claramente o rótulo “fonte declarada, não autenticada”.

## 5. Plano recomendado

### Fase 1 — Correção de baseline e documentação

**Objetivo:** remover ambiguidades e estabelecer critérios de aceite.

1. Atualizar `docs/SRS.md` para refletir a IA local já integrada.
2. Atualizar Roadmap, Architecture, Security, README e CHANGELOG de forma consistente.
3. Definir a versão da Alpha e a política de suporte.
4. Criar uma matriz de validação Windows com OS/build, hardware, permissões, scanner, resultado esperado e evidência.
5. Registrar explicitamente os limites que continuam fora do escopo: reparos reais, restauração real, Enterprise, timeline causal e PDF nativo.

**Critério de saída:** uma pessoa nova no projeto consegue entender o comportamento real apenas lendo a documentação.

### Fase 2 — Validação E2E em hosts Windows

**Objetivo:** transformar riscos conhecidos em evidências reproduzíveis.

Matriz mínima:

| Cenário | Evidência esperada |
|---|---|
| Windows 11 x64 limpo | app inicia, XAML carrega e diagnóstico termina |
| Windows 11 x64 com falhas/serviços variados | estados corretos, sem falso Healthy |
| execução sem elevação | mensagens de indisponibilidade/acesso negado corretas |
| Ollama ausente | estado orientativo, sem crash |
| Ollama/modelo disponível | preview, envio local, resposta e cancelamento |
| cancelar diagnóstico/IA/WSB | operação interrompida sem estado obsoleto |
| CBS.log válido, inválido e acima do limite | observação isolada, sem persistência indevida |
| Server 2019 com WSB | contrato runtime de `Get-WBBackupSet` validado |
| histórico desligado/ligado | persistência, redação e retenção corretas |
| exportação HTML | escape, ausência de recursos remotos e conteúdo esperado |

**Critério de saída:** relatório versionado com evidências, limitações e incidentes; não apenas “CI verde”.

### Fase 3 — UX, acessibilidade e responsividade

**Objetivo:** tornar a Alpha utilizável por técnicos no dia a dia.

1. Trocar a navegação lateral fixa por comportamento adaptativo em largura reduzida.
2. Fazer os cards de resumo refluírem de 3 colunas para 1 coluna.
3. Fazer categorias/achados refluírem de 2 colunas para 1 coluna.
4. Permitir que barras de ações quebrem linha ou usem layout vertical.
5. Mover conteúdo avançado para `Expander`/seções recolhíveis.
6. Definir `AutomationProperties.Name`/descrições onde o texto visual não for suficiente.
7. Validar Tab order, foco visível, seleção de texto, contraste, escala de texto e estados de carregamento/erro.
8. Testar pelo menos 1024×768, 1280×720, 1366×768 e janela estreita redimensionada.

**Critério de saída:** nenhuma sobreposição, corte ou ação inacessível nos tamanhos definidos; screenshots e checklist de UI anexados ao PR.

### Fase 4 — Release Alpha reproduzível

**Objetivo:** permitir que terceiros testem sem depender do ambiente de desenvolvimento.

1. Publicar artefato em Release do GitHub ou documentar formalmente o fluxo de Actions.
2. Gerar SHA-256 do ZIP.
3. Versionar o pacote e o changelog.
4. Manter o aviso de publicador não confiável enquanto não houver assinatura.
5. Incluir instruções de diagnóstico de falha e coleta mínima de feedback.
6. Decidir sobre .NET 9 versus upgrade antes do fim do suporte documentado.

## 6. O que não recomendo agora

- Implementar reparos automáticos antes da validação dos scanners e dos verificadores de pré/pós-condição.
- Implementar restauração real de backup antes do smoke test de WSB em Server 2019 e de uma revisão de segurança específica.
- Começar a edição Enterprise sem definir identidade, autorização, transporte, multi-tenant, telemetria, retenção e modelo de ameaça.
- Adicionar mais fontes de conhecimento sem processo de revisão/proveniência.
- Expandir a IA para serviço remoto ou permitir que ela gere ações executáveis.
- Declarar suporte para Windows Server, Windows 10 ou ARM com base apenas no build da CI.

## 7. Backlog sugerido por prioridade

### P0

- [ ] Alinhar a SRS com a integração Ollama que já está no `main`.
- [ ] Criar matriz e protocolo de validação E2E em Windows 11 x64.
- [ ] Validar o fluxo completo em pelo menos um host Windows real.
- [ ] Validar `Get-WBBackupSet` em Windows Server 2019 ou retirar esse item da promessa operacional visível.

### P1

- [ ] Corrigir reflow dos grids e da navegação em janelas estreitas.
- [ ] Reduzir a densidade da Home com seções recolhíveis.
- [ ] Executar checklist de acessibilidade e teclado.
- [ ] Testar cancelamento e recuperação de erro nos fluxos longos.
- [ ] Publicar Alpha versionada com checksum e notas de release.

### P2

- [ ] Decidir upgrade de .NET.
- [ ] Projetar assinatura/proveniência dos knowledge packs.
- [ ] Criar comparador de diagnósticos somente depois de haver histórico confiável e sem inferir causalidade.
- [ ] Avaliar PDF nativo como item de conveniência, não como prioridade de confiabilidade.

## 8. Próximo incremento recomendado

O próximo PR deveria ser pequeno e focado em **“baseline da Alpha validada”**, contendo:

1. atualização documental da IA local;
2. checklist/matriz de validação Windows;
3. correções de responsividade da navegação e dos grids;
4. testes automatizados de estados e layout onde possível;
5. documentação do resultado da validação real.

Depois desse PR, o projeto estará em melhor posição para decidir entre aprofundar a ferramenta desktop, investir em distribuição/assinatura ou iniciar uma linha Enterprise separada.

## 9. Conclusão

O projeto está tecnicamente bem estruturado para uma Alpha local e demonstra cuidado incomum com privacidade, fail-closed e contenção de ações. O avanço de maior retorno agora é **reduzir incerteza**, não aumentar superfície funcional: alinhar a documentação, validar no Windows real e tornar a UI robusta em diferentes larguras. Com esses gates concluídos, decisões sobre reparo, backup real, assinatura e Enterprise poderão ser tomadas com evidência, não apenas com base na compilação e nos testes simulados.
