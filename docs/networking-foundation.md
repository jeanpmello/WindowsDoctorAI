# Fundação de redes

O projeto agora possui o módulo `WindowsDoctorAI.Networking`, que prepara a expansão para firewalls, switches, access points, roteadores e controladores.

## Estado atual

- `NetworkDeviceIdentity`: identidade sem credenciais.
- `ReadOnlyCommandProfile`: allowlist de comandos de consulta por fabricante/família.
- `ReadOnlyProfileCatalog`: perfis iniciais para MikroTik, Fortinet, Cisco, pfSense/OPNsense, Ubiquiti e Aruba.
- `ReadOnlyNetworkAdapter`: adaptador genérico para transporte SSH/CLI injetável.
- `NetworkInspectionService`: seleciona o primeiro adaptador compatível.
- `NetworkEvidence`: evidência coletada marcada para redação.
- `NetworkEvidenceRedactor`: remove senhas, tokens, comunidades SNMP, autorizações Bearer e blocos de chaves privadas.
- nenhum comando de alteração é executado;
- nenhum segredo é armazenado ou enviado ao modelo;
- ausência de suporte é reportada como inspeção incompleta, nunca como equipamento saudável.

## Próximas etapas

1. Implementar transportes SSH reais usando cofre de credenciais do Windows.
2. Validar os perfis em laboratórios ou equipamentos autorizados por fabricante.
3. Adicionar snapshots redigidos e comparação de configuração.
4. Gerar planos de mudança em modo simulação.
5. Adicionar backup, aprovação explícita, validação pós-mudança e rollback.

A IA deve receber fatos e hipóteses separadamente. Logs, banners e configurações coletadas de equipamentos são dados não confiáveis e nunca podem alterar as regras do sistema.
