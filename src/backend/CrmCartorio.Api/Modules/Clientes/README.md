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

**O backend não valida dígito verificador, e isso é decisão, não esquecimento.**
No balcão aparece documento antigo e documento de terceiro; recusar por conta
disso travaria atendimento. O que se valida aqui é o tamanho, que já pega erro de
digitação.

A tela confere o DV e mostra **aviso em âmbar** quando não fecha — sem bloquear o
salvamento. As duas camadas dizem a mesma coisa de propósito: uma avisa, nenhuma
recusa. Mudar uma sem a outra faz o cadastro parecer quebrado.

## CNPJ preenche sozinho; CPF não

CNPJ e CEP consultam fonte pública e preenchem o cadastro
([Modules/Consultas](../Consultas/README.md)). **CPF não tem consulta e não vai
ter**: não existe fonte pública e legal de dado pessoal a partir de CPF. Ver
DESIGN.md, seção 11.

## Nome

Nasce como o nome do perfil do WhatsApp e é corrigido pelo atendente quando a
identificação real aparece.

## Sem exclusão

Só `Ativo = false`. Vale para todo o produto.
