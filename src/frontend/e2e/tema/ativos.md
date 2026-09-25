# Imagens para o tema escuro

Geradas com o image_gen integrado, a partir dos arquivos originais deste repositorio.
Os originais do tema claro nao foram substituidos. Exportacao WebP com alpha preservado
nas marcas, e reducao de resolucao para uso na interface, sem redesenho programatico.

Arquivos finais em `src/frontend/public/brand/`:

- `crm-cartorio-logo-escuro.webp`: assinatura transparente, 1230x410.
- `crm-cartorio-simbolo-escuro.webp`: simbolo transparente, 304x304.
- `login-atendimento-escuro.webp`: fotografia, 1536x1024.

O componente MarcaDoProduto reserva a mesma proporcao nos dois temas. A assinatura
escura inclui margem transparente vertical no arquivo, compensada por object-fit
no CSS. O simbolo tambem e usado pelo favicon. Fotografias de clientes e anexos
nao recebem filtros de cor: apenas os ativos de identidade do produto mudam.

## Prompt da fotografia

Edit this existing CRM Cartorio login background photograph, keeping the SAME composition, landscape 3:2 framing, tablet placement at the right, camera perspective, realistic materials and messaging concept. Purpose: a refined dark-theme product login background. Change the brown wood desktop to neutral charcoal/black textured wood with understated realistic grain. Change the tablet display from ivory to dark graphite #171b22, incoming message cards to #252b34, outgoing message cards to rich blue #2865bc. Keep message line placeholders (not actual words), their subtle light gray contrast, the small bot and person icons and bottom input. Use subtle cool neutral studio light so the tablet is clearly legible and its metal edges defined; avoid pitch-black crushing or dramatic neon. Left half must stay quiet, mostly empty, neutral charcoal for the existing login form overlay. No logos, extra text, watermark, orbs, glows, gradients or decorative bokeh. Preserve crisp product details; premium natural product photography, no saturated blue cast over the whole image. Produce this as a production background image, not a screenshot or UI mockup.

## Prompt da assinatura

Production asset edit: create the dark-theme colorway of this EXACT existing 'CRM Cartório' horizontal brand logo. Keep exact wording including ó accent, exact serif wordmark style, symbol silhouette of document/speech bubble with folded upper right corner, divider, proportions, horizontal arrangement, and tight framing. Color changes only: burgundy outside frame becomes refined medium cornflower/royal blue #508de5 with restrained dimensional shading; document remains soft pale silver #edf0f5 with dark graphite lines; folded gold corner becomes cool pale blue/silver; thin divider becomes subdued blue-gray; 'CRM' letters become blue #83b8ff and 'Cartório' letters become near-white #edf0f5, highly legible on charcoal. REAL transparent background with alpha, no white rectangle, no black rectangle, no visible checkerboard, no glow, no cast shadow beyond symbol, no additional shapes or labels. Preserve the brand faithfully; do not redesign or change typography. Output wide tightly cropped horizontal logo with very little transparent padding, not a presentation board.

## Prompt do simbolo

Image 1 is the EDIT TARGET: original square CRM Cartorio symbol. Image 2 is COLOR REFERENCE ONLY: approved blue and pale silver dark-theme brand logo. Produce a square app brand symbol matching the exact shape and tight framing of image 1, recolored to match the symbol in image 2. Burgundy frame becomes royal blue #508de5/#2865bc, white document becomes soft silver #edf0f5, gold folded corner becomes icy pale blue/silver, three document lines remain dark graphite. Preserve original document/speech-bubble silhouette and original proportions. REAL fully transparent alpha background; no white tile, no dark tile, no border, no checkerboard, no glow, no extra padding, no text or divider. The mark should fill approximately 90% of square canvas height, with clean antialiased edges, subtle existing dimensional shading, professional small-size legibility.
