# Especificação de Requisitos de Software (SRS)

**Produto:** Windows Doctor AI
**Versão deste documento:** 0.1 (baseline proposta)
**Estado:** requisitos orientadores. O Milestone 1 implementa a solução inicial, interface, coleta de inventário, configuração local e histórico SQLite; requisitos completos de achados, score real, reparo, IA e compatibilidade de produção continuam pendentes.

## 1. Introdução

O Windows Doctor AI é uma aplicação local para inventário e diagnóstico assistido de computadores Windows. Este documento registra requisitos para orientar etapas do roadmap e não certifica compatibilidade com versões específicas do Windows.

### 1.1 Objetivo

Organizar sinais de sistema e hardware em resultados compreensíveis, apoiar a investigação técnica e, numa evolução futura, permitir correções controladas após aprovação explícita.

### 1.2 Escopo do Milestone 1

A primeira entrega contém uma interface WinUI 3, scanner local de inventário, score demonstrativo, histórico local SQLite opcional e telas Inicial, Sobre e Configurações. A coleta é somente de leitura. IA, execução de reparos, gestão remota e Health Score real não estão disponíveis.

O código .NET 9/WinUI 3 requer validação de execução em Windows; testes unitários de scanner usam fonte simulada, sem substituir testes com WMI real.

## 2. Requisitos para evolução

Os requisitos funcionais e não funcionais detalhados das próximas etapas deverão continuar definindo evidência, gravidade, retenção, consentimento, privacidade, compatibilidade, atualização do schema e controle de reparos. Consulte [Architecture](Architecture.md), [Security](Security.md), [Database](Database.md) e [Roadmap](Roadmap.md) para o estado entregue e as limitações.
