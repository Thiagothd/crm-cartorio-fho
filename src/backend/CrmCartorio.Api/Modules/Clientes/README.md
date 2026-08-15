# Módulo: Clientes

O contato final que manda mensagem para o cartório. **Não é usuário do sistema.**

## O telefone é a identidade

`TelefoneWhatsapp` é obrigatório e único por tenant, guardado em E.164 sem
símbolos (`5538999000045`). É por ele que a mensagem chega — sem telefone não há
conversa.

O serviço aceita o número como o balcão digita (`38999000045`) e completa o `55`
sozinho. Ninguém digita "+55" no meio de um atendimento.

## O documento é opcional de propósito

Exigir CPF no primeiro "bom dia" trava o atendimento. Quando informado, é
validado (11 ou 14 dígitos) e guardado só com dígitos.

## Nome

Nasce como o nome do perfil do WhatsApp e é corrigido pelo atendente quando a
identificação real aparece.

## Sem exclusão

Só `Ativo = false`. Vale para todo o produto.
