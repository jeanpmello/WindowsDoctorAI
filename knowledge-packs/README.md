# Pacotes de conhecimento Windows

Importe primeiro `microsoft-windows-update-pilot.json` (schema 1.3) para instalar as duas regras históricas: `0xC1900107` de Windows Setup e `0x80073712` de Windows Update. O piloto 1.3 permanece preservado, sem substituição ou atualização automática.

Depois, importe `microsoft-windows-update-pilot-1.4.json` para atualizar `0x80073712`: o pacote contém somente duas regras v2, uma para Windows Client (`ProductType=1`, build mínimo inclusivo 10240) e outra, com ID distinto, para Windows Server (`ProductType=2` ou `3`, build mínimo inclusivo 14393). Nenhuma declara build máximo. Com inventário conhecido, o engine verifica família e build antes de recomendar; inventário ausente ou inválido falha fechado. Os critérios, evidências e procedimentos continuam manuais e exigem confirmação do usuário.

`0xC1900107` não está no pacote 1.4 e continua na regra histórica 1.3 com aplicabilidade em texto livre **não avaliada automaticamente**. Não há escopo de sistema operacional/build declarado para esse código, portanto não se deve inferir nem adicionar um alvo estruturado.
