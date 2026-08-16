# Módulo: Consultas

Preenchimento assistido de cadastro a partir de fonte pública. **Não tem entidade
e não tem tabela** — não lê nem grava nada do cartório, logo não há `TenantId` a
filtrar. É o único módulo nessa condição.

## CPF não se consulta

**Não existe rota de CPF e não vai existir.** Não há fonte pública e legal que
devolva dado pessoal a partir de CPF; o que se encontra por aí é base vazada ou
revenda irregular, e plugar uma dessas num sistema que guarda dado de cartório
seria trocar o argumento de LGPD do produto por uma conveniência de digitação.

O que o CPF ganha é conferência de dígito verificador **na tela**, como aviso —
nunca como bloqueio. Ver `lib/mascaras.ts` no frontend e a seção 11 do
[DESIGN.md](../../../../../docs/DESIGN.md).

CNPJ é outra coisa: dado de empresa é público por definição, e a Receita publica.

## Por que passa pelo servidor

A tela poderia chamar a BrasilAPI direto do navegador. Não chama, por três
motivos:

1. **Cache da serventia inteira.** Cartório atende a mesma cidade o dia todo. O
   mesmo punhado de CEP responde de memória — e continua respondendo com o link
   caído.
2. **Uma máquina com internet, não todas.** Estação de cartório costuma estar
   atrás de rede fechada.
3. **O endereço do terceiro fica num `appsettings`.** Dá para trocar de provedor
   ou desligar tudo (`ConsultaPublica:Habilitada = false`) sem tocar em código.

## Provedores

| Consulta | Principal | Reserva |
| --- | --- | --- |
| CEP | BrasilAPI `/cep/v1/{cep}` | ViaCEP `/{cep}/json/` |
| CNPJ | BrasilAPI `/cnpj/v1/{cnpj}` | — (o ViaCEP não faz CNPJ) |

O formato de cada provedor fica **privado** ao serviço: BrasilAPI devolve campo em
inglês, ViaCEP em português, e os dois viram `EnderecoConsultado`. Trocar de
provedor não mexe no frontend.

Um "não encontrado" da BrasilAPI não encerra o assunto — as bases divergem em CEP
de distrito e povoado, que é exatamente o caso de Gameleiras.

## Falha nunca trava cadastro

Todo caminho de erro devolve código estável e a tela segue editável:

| Código | HTTP | Quando |
| --- | --- | --- |
| `cep_invalido` / `cnpj_invalido` | 400 | Não tem 8 / 14 dígitos |
| `consulta_nao_encontrada` | 404 | Algum provedor respondeu, e respondeu que não existe |
| `consulta_indisponivel` | 503 | Nenhum provedor respondeu, ou a consulta está desligada |

A distinção entre 404 e 503 é o ponto: dizer "CEP não existe" quando o que caiu
foi a rede manda o atendente procurar erro que não há.
