# Assistente de IA do Windows Doctor AI

## Decisão de modelo

O padrão de desenvolvimento local foi alterado para `qwen3-vl:8b`.

O Ollama é apenas uma ponte temporária para desenvolvimento. A distribuição final não deve exigir Ollama instalado separadamente; a estratégia de runtime e modelo embutidos está documentada em [self-contained-ai-distribution.md](self-contained-ai-distribution.md).

Motivos:

- aceita texto e imagens no mesmo modelo;
- pode conversar sobre o diagnóstico e interpretar screenshots de erro;
- possui contexto longo para histórico e evidências;
- a variante 8B é um compromisso entre qualidade e consumo local.

A página oficial do Ollama lista `qwen3-vl:8b` com aproximadamente 6,1 GB e contexto de 256K. A variante `qwen3-vl:4b` (aproximadamente 3,3 GB) deve ser usada em máquinas mais modestas. A variante 30B/32B exige muito mais memória e não é o padrão da aplicação.

O Qwen3-VL exige Ollama 0.12.7 ou superior conforme a página oficial do modelo. Antes de usar visão:

```powershell
ollama pull qwen3-vl:8b
```

Para uma máquina com pouca memória:

```powershell
ollama pull qwen3-vl:4b
```

A configuração continua em loopback (`127.0.0.1`/`localhost`). Nenhum inventário ou screenshot é enviado para serviço remoto pela aplicação.

## Capacidades planejadas e implementadas nesta fatia

### Diagnóstico contextual

`DiagnosticConversationPromptBuilder` monta uma conversa com:

- diagnóstico atual redigido;
- histórico curto da conversa;
- mensagem atual do usuário;
- regras explícitas para diferenciar fatos, hipóteses e recomendações.

O histórico é limitado a 12 mensagens e cada mensagem a 4.000 caracteres.

### Conversa

`IDiagnosticAiConversationProvider.ChatAsync` envia mensagens estruturadas para `/api/chat` do Ollama, sem streaming e com limites de quantidade/tamanho. A resposta é truncada pelo mesmo limite de segurança já usado na análise atual.

A conversa não autoriza:

- execução de comandos;
- alteração de Registro, serviços ou arquivos;
- instalação de software;
- download de scripts;
- elevação de privilégio;
- confirmação de que o computador foi corrigido.

### Identificação de telas de erro

`AnalyzeScreenshotAsync` aceita PNG, JPEG ou WebP até 8 MiB e envia a imagem como base64 no campo `images` da mensagem do Ollama.

A instrução da análise exige que o modelo extraia somente:

- texto legível;
- código de erro visível;
- aplicativo/tela;
- evidências observadas;
- hipóteses explicitamente marcadas;
- próximos passos manuais.

Uma screenshot nunca é tratada como prova suficiente de causa. O usuário deve poder revisar a imagem e a resposta antes de agir.

## Busca de soluções

A busca de soluções deve seguir esta ordem:

1. regras importadas e validadas na Knowledge Base;
2. referências HTTPS declaradas pela regra;
3. fontes oficiais verificadas por um componente de busca separado;
4. conversa com o modelo apenas para explicar, comparar e priorizar os resultados encontrados.

A IA não deve fingir que pesquisou a internet. Se não houver regra ou fonte verificada disponível, ela deve dizer isso claramente.

A próxima etapa para busca online é criar um `IOfficialSolutionSearchProvider` com:

- allowlist inicial de `learn.microsoft.com`, `support.microsoft.com` e `techcommunity.microsoft.com`;
- consulta baseada em códigos/sintomas estruturados, não no inventário bruto;
- redação antes do envio;
- cache local com validade explícita;
- URL, título, data e trecho preservados como evidência;
- bloqueio de conteúdo que proponha execução automática;
- revisão do usuário antes de abrir ou compartilhar resultados.

Essa busca não deve ser implementada como “o modelo pode navegar livremente”.

## Fluxo recomendado para o usuário

1. Executar o diagnóstico local.
2. Revisar achados e evidências.
3. Conversar com a IA sobre um achado específico.
4. Selecionar uma screenshot de erro, se houver.
5. Pedir identificação do texto/código visível.
6. Consultar soluções da Knowledge Base e fontes oficiais.
7. Revisar riscos, privilégios, backup e rollback.
8. Executar manualmente ou, em uma fase futura, usar uma ação catalogada com confirmação.
9. Executar novamente o diagnóstico para verificar evolução.

## Instalação local

```powershell
ollama --version
ollama pull qwen3-vl:8b
ollama list
```

O modelo pode ser trocado em `src/WindowsDoctorAI.App/appsettings.json`:

```json
{
  "Ai": {
    "Ollama": {
      "Enabled": true,
      "BaseUrl": "http://127.0.0.1:11434",
      "Model": "qwen3-vl:8b"
    }
  }
}
```

## Limites de confiança

O assistente não deve declarar:

- causa confirmada sem evidência suficiente;
- que um comando foi executado;
- que o sistema foi corrigido;
- que uma fonte não verificada é oficial;
- que uma screenshot contém informação que não está legível;
- que uma regra importada é verdadeira apenas porque tem hash.

O produto deve sempre separar **observação**, **hipótese**, **fonte** e **ação**.

## Referências

- [Qwen3-VL no Ollama](https://ollama.com/library/qwen3-vl)
- [Gemma 3 no Ollama](https://ollama.com/library/gemma3)
- [Ollama Vision](https://docs.ollama.com/capabilities/vision)
- [Ollama Chat API](https://docs.ollama.com/api/chat)
