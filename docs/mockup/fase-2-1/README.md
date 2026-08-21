# Fase 2.1 — Comparação visual de branding e login

> **Status:** correção implementada; aguardando validação visual final do cliente.
> **Última atualização:** 2026-08-20.

## Evidências

| Estado | Captura |
| --- | --- |
| Fase 2, antes da correção | [antes-login-1440.png](antes-login-1440.png) |
| Fase 2.1, após a correção | [depois-login-1440.png](depois-login-1440.png) |

O estado anterior exibia fundo opaco na assinatura, excesso de área vazia e
painel institucional sem relação direta com o atendimento digital. A correção
usa marca transparente, fotografia full-screen de mensageria e bloco de acesso
compacto ao lado do tablet. O título visual e o campo da serventia foram
removidos; autenticação, sessão, redirecionamento e permissões foram preservados.

## Matriz responsiva

- [1024 × 768](depois-login-1024.png)
- [768 × 1024](depois-login-768.png)
- [360 × 800](depois-login-360.png)

Nos três pontos não há overflow, rolagem ou corte. A assinatura horizontal é
sempre reduzida a partir do raster oficial de 1188 × 292, e o fundo usa o WebP
de 1536 × 1024 sem ampliação em DPR 1.

## Branding relacionado

A sidebar usa o símbolo oficial isolado sobre o fundo escuro, sem placa branca.
No rail recolhido, o item ativo permanece transparente e é indicado por uma
linha bordô; o controle para reabrir o menu fica abaixo da marca para não
sobrepor o símbolo. Favicons próprios existem em 16, 32, 48, 180 e 192 px.
