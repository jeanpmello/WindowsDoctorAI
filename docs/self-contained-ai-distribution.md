# Estratégia de distribuição independente da IA

## Decisão

O Windows Doctor AI não deve depender de uma instalação separada do Ollama, Python, Node.js, servidor local ou chave de API.

A distribuição final deverá conter:

1. o aplicativo WinUI;
2. o runtime local de inferência;
3. o modelo de IA;
4. os metadados de versão, licença e SHA-256;
5. o verificador de integridade e compatibilidade.

O usuário deverá executar o instalador e abrir o aplicativo. Nenhum comando adicional será necessário.

## Tecnologia recomendada

A rota principal será **Windows ML/ONNX Runtime GenAI**:

- é a tecnologia recomendada pela Microsoft para inferência local no Windows;
- pode executar em CPU, GPU e, quando disponível, NPU;
- tem APIs .NET/C#;
- suporta modelos de linguagem e modelos multimodais na matriz do ONNX Runtime GenAI;
- permite distribuir o runtime dentro do pacote ou usar componentes do Windows quando a política de compatibilidade permitir;
- mantém os dados no computador e funciona offline.

A dependência de Ollama permanece apenas como ponte temporária para desenvolvimento e comparação de respostas. Ela não será requisito do instalador final.

## Modelo e tamanhos

A distribuição precisa ter perfis para máquinas diferentes:

| Perfil | Modelo | Conteúdo | Objetivo |
|---|---|---|---|
| Compacto | modelo multimodal quantizado pequeno | texto + screenshot | computadores com pouca memória |
| Padrão | modelo multimodal quantizado médio | conversa + diagnóstico + screenshot | recomendação geral |
| Avançado | modelo maior opcional | melhor qualidade | máquinas com GPU/memória suficientes |

O instalador deve informar o tamanho real do pacote. Não devemos esconder que um modelo multimodal pode adicionar vários gigabytes ao download.

O modelo não será versionado no Git porque isso torna o repositório impraticável. Ele será publicado como asset de uma Release do GitHub, com:

- URL da release;
- versão do modelo;
- licença;
- SHA-256;
- arquitetura suportada;
- memória mínima recomendada;
- contexto máximo;
- data de publicação.

O instalador incorporará o asset para a edição completa. Um pacote de desenvolvimento poderá omitir o modelo, mas isso não será chamado de produto completo.

## Fluxo de instalação final

```text
WindowsDoctorAI-Setup.exe
        |
        +-- instala o aplicativo
        +-- instala o runtime local incluído
        +-- instala o modelo incluído
        +-- verifica SHA-256
        +-- verifica Windows/arquitetura/memória
        +-- cria atalho
        +-- inicia o aplicativo
```

Não haverá `ollama pull`, `pip install`, download manual ou configuração de servidor.

## Validação no primeiro uso

Antes de habilitar a conversa, o app deve verificar:

- existência dos arquivos do runtime;
- existência dos arquivos do modelo;
- hash esperado;
- arquitetura x64/ARM64;
- versão mínima do Windows;
- memória disponível estimada;
- suporte de execução selecionado;
- espaço livre suficiente;
- licença e versão do modelo.

Se a IA não puder ser iniciada, a tela deve explicar o motivo e continuar oferecendo diagnóstico determinístico. Nunca apresentar “saudável” apenas porque a IA está indisponível.

## Arquitetura de código

A aplicação deve depender de uma porta estável:

```text
IDiagnosticAiProvider
        |
        +-- EmbeddedOnnxGenAiProvider   (distribuição final)
        +-- OllamaDiagnosticAiProvider  (desenvolvimento/compatibilidade)
        +-- FakeDiagnosticAiProvider    (testes)
```

A UI não conhece Ollama, ONNX, Windows ML ou caminhos de modelo. Ela conversa com a porta e recebe estados como:

- `Ready`;
- `RuntimeMissing`;
- `ModelMissing`;
- `IntegrityFailure`;
- `UnsupportedHardware`;
- `OutOfMemoryRisk`;
- `ProviderUnavailable`;
- `Completed`;
- `Cancelled`.

## Conversa, busca e telas de erro

O runtime embutido deverá sustentar:

- conversa contextual com o diagnóstico atual;
- histórico curto em memória;
- análise de screenshots PNG/JPEG/WebP;
- extração de códigos e texto legível;
- resposta em português do Brasil;
- explicação baseada em evidências.

A busca de soluções continua sendo uma camada separada. A IA não deve navegar livremente nem inventar uma solução. A ordem será:

1. Knowledge Base local;
2. referências HTTPS declaradas;
3. busca oficial allowlistada, se o usuário permitir internet;
4. modelo para explicar e priorizar os resultados.

## Fases de implementação

### Fase A — abstração e compatibilidade

- manter o provider Ollama existente para desenvolvimento;
- adicionar a porta de runtime embutido;
- criar estados de disponibilidade e integridade;
- criar testes com provider falso;
- evitar que a UI dependa do Ollama.

### Fase B — runtime embutido

- adicionar o pacote C# do ONNX Runtime GenAI/Windows ML;
- executar um modelo textual quantizado incluído no pacote;
- validar geração, cancelamento e limites;
- empacotar DLLs nativas no publish self-contained;
- medir memória e tempo de primeira resposta.

### Fase C — visão e modelo multimodal

- adicionar o modelo multimodal em formato compatível;
- validar screenshot real de erro;
- verificar CPU/GPU/DirectML;
- criar fallback textual quando visão não estiver disponível;
- não transformar OCR em diagnóstico causal automático.

### Fase D — instalador completo

- publicar runtime + modelo em Release asset;
- gerar instalador ou pacote completo;
- validar hash e licença;
- executar smoke test sem Ollama instalado;
- atualizar `README-ALPHA.md` com requisitos reais;
- criar matriz Compacto/Padrão/Avançado.

## Critério de pronto

A tarefa só será considerada concluída quando uma máquina Windows limpa conseguir:

1. instalar o pacote;
2. iniciar o app sem Ollama;
3. executar diagnóstico determinístico;
4. conversar com a IA local;
5. analisar uma screenshot de erro;
6. funcionar offline;
7. informar claramente limitações de memória/compatibilidade;
8. passar pelo smoke test do CI.

Até esse critério, o Ollama deve ser descrito apenas como ponte de desenvolvimento, não como requisito da distribuição final.

## Referências técnicas

- [Windows ML overview](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/overview)
- [Windows ML: distribuir modelos](https://learn.microsoft.com/en-us/windows/ai/models/)
- [ONNX Runtime GenAI](https://onnxruntime.ai/docs/genai/)
- [ONNX Runtime GenAI no GitHub](https://github.com/microsoft/onnxruntime-genai)
- [Pacote C# Microsoft.ML.OnnxRuntimeGenAI](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntimeGenAI)
