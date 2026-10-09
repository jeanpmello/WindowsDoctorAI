# Projetos públicos que podem acelerar o Windows Doctor AI

Há componentes públicos relevantes, mas eles têm papéis diferentes. A recomendação não é copiar um aplicativo pronto inteiro; é reutilizar runtimes e exemplos bem mantidos, mantendo o diagnóstico, a privacidade e a UI sob controle do Windows Doctor AI.

## 1. Microsoft ONNX Runtime GenAI — candidato principal

Repositório: <https://github.com/microsoft/onnxruntime-genai>

Por que ajuda:

- API para geração local em ONNX;
- suporte a Python, C#, C e C++;
- implementa tokenização, geração, sampling, cache KV e structured output;
- matriz pública inclui modelos de linguagem e visão, incluindo famílias Qwen e Phi;
- possui caminhos para CPU, DirectML e outras acelerações.

Como usar no projeto:

- adicionar um adaptador `EmbeddedOnnxGenAiProvider` atrás de `IDiagnosticAiProvider`;
- distribuir o runtime nativo junto do publish Windows;
- distribuir o modelo como asset da Release, não como arquivos no Git;
- controlar cancelamento, limite de contexto, memória e integridade do modelo;
- usar o modelo multimodal compatível para conversa e screenshots.

Riscos:

- a API GenAI é indicada como preview e pode mudar;
- modelos precisam estar no formato compatível e corretamente convertidos/quantizados;
- visão exige validação específica do modelo escolhido;
- o pacote final pode ficar com vários gigabytes.

Decisão: **primeira opção para a edição final embutida**.

## 2. Windows ML — base oficial do Windows

Repositório: <https://github.com/microsoft/WindowsML>

Documentação: <https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/overview>

Por que ajuda:

- é a camada recomendada pela Microsoft para inferência local no Windows;
- pode usar CPU, GPU e NPU conforme o dispositivo;
- permite runtime compartilhado do Windows ou distribuição self-contained;
- evita que o app tenha de carregar todos os providers específicos de hardware.

Como usar no projeto:

- avaliar Windows ML como backend do adaptador embutido;
- manter fallback CPU para hardware sem aceleração;
- declarar claramente a versão mínima do Windows;
- deixar o modelo dentro do pacote completo ou em asset validado da Release.

Riscos:

- recursos e providers dependem da versão do Windows e do hardware;
- não elimina o tamanho do modelo;
- precisamos testar x64, GPU integrada, GPU dedicada e máquina sem aceleração.

Decisão: **camada Windows a ser avaliada junto com ONNX Runtime GenAI**.

## 3. Microsoft Foundry Local — referência de produto e integração

Repositório: <https://github.com/microsoft/Foundry-Local>

Por que ajuda:

- mostra uma solução local ponta a ponta;
- fornece referências de descoberta de modelos, sessão, chat e ciclo de vida;
- ajuda a comparar UX, estados de disponibilidade e descarregamento de modelos.

Por que não é a dependência final neste momento:

- o fluxo público normalmente envolve instalação/serviço ou download separado do modelo;
- a meta do Windows Doctor AI é abrir após a instalação sem exigir outro runtime;
- adicionar um serviço externo à instalação pode aumentar pontos de falha e suporte.

Decisão: **usar como referência e benchmark de produto, não como requisito do instalador inicial**.

## 4. llama.cpp — fallback pragmático

Repositório: <https://github.com/ggml-org/llama.cpp>

Por que ajuda:

- runtime nativo amplamente usado;
- foco em execução local com pouca infraestrutura;
- suporte a LLM/VLM e múltiplos backends;
- pode ser empacotado como DLL junto com o app.

Riscos:

- integração C/C++/PInvoke aumenta a superfície de manutenção;
- precisamos controlar ciclo de vida nativo, cancelamento e erros;
- suporte a cada família multimodal/modelo deve ser validado individualmente;
- atualizações e licenças dos modelos permanecem responsabilidade do produto.

Decisão: **fallback se o caminho ONNX/Windows ML não suportar o modelo multimodal escolhido com qualidade suficiente**.

## 5. Ollama — somente ponte de desenvolvimento

Repositório: <https://github.com/ollama/ollama>

Ajuda a testar rapidamente prompts e modelos, mas não atende ao requisito de entrega independente se o usuário tiver de instalar Ollama ou executar `ollama pull`.

Decisão: **não será requisito do produto final**.

## Recomendação final

A ordem recomendada é:

```text
Windows ML / ONNX Runtime GenAI
        ↓ se modelo multimodal compatível estiver validado
runtime + modelo incluídos no pacote
        ↓ se houver bloqueio de conversão/visão
llama.cpp embutido como fallback
        ↓ apenas para desenvolvimento e comparação
Ollama
```

## O que não devemos reutilizar sem revisão

- aplicativos completos que executam comandos de sistema;
- agentes que aceitam shell arbitrário;
- prompts que prometem “otimizar” o Windows automaticamente;
- downloaders sem hash, licença ou origem verificável;
- modelos distribuídos sem licença clara;
- bibliotecas que enviam inventário para nuvem por padrão;
- scripts de instalação que alteram serviços, registro ou políticas.

## Próxima implementação no Windows Doctor AI

1. criar `EmbeddedOnnxGenAiProvider` atrás de uma interface estável;
2. adicionar pacote C# do ONNX Runtime GenAI em uma branch de experimento;
3. testar um modelo textual pequeno primeiro;
4. medir memória, primeira resposta, cancelamento e publish self-contained;
5. testar um modelo multimodal para screenshots;
6. atualizar o workflow Alpha para incluir runtime e modelo como Release asset;
7. somente então retirar Ollama da documentação de requisitos.

## Referências consultadas

- <https://github.com/microsoft/onnxruntime-genai>
- <https://github.com/microsoft/WindowsML>
- <https://github.com/microsoft/Foundry-Local>
- <https://github.com/ggml-org/llama.cpp>
- <https://github.com/ollama/ollama>
- <https://onnxruntime.ai/docs/genai/>
- <https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/overview>
