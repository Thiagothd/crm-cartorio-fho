# Módulo: Tenants

O cartório cliente.

Na Fase 1 existe **um** registro — o cartório do cartório parceiro. O `TenantId` já está em
toda entidade de negócio e toda consulta já filtra por ele.

## Por que carregar isso desde já

Adicionar isolamento por tenant depois obrigaria a revisar cada consulta já
escrita, com risco de vazar dado de um cartório para outro. É barato agora e caro
depois — e num produto que vende soberania de dados como diferencial, esse é o
último lugar onde vale a pena economizar.

## Campos de WhatsApp

`WhatsappBusinessAccountId`, `WhatsappPhoneNumberId` e `WhatsappNumeroExibicao`
ficam nulos no piloto não oficial. São o ponto de encaixe da Cloud API — dependem
de CNPJ verificado na Meta e de número dedicado, ambos processos externos.

`WhatsappInstancia` é o vínculo atual com a Evolution. O nome chega no webhook,
identifica o tenant e é gravado pela tela de pareamento. Cada ambiente usa uma
instância própria (`cartorio-piloto` em produção e `cartorio-teste` em teste);
uma mesma instância nunca pode pertencer a dois tenants.

## Fase 3

O cadastro de tenant hoje é feito pelo seed. O onboarding automático — incluindo
o Embedded Signup da Meta, para que cada cliente verifique a própria conta com o
próprio CNPJ — é escopo da Fase 3.
