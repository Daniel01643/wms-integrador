# Verificação da fase DOCS: Puxada TSP

Data: 12/11/2026. Não existia `review-docs.json` (primeira iteração, feita do zero).

## O que foi entregue
- `docs/ARQUITETURA.md` (§2.2), `docs/OPERACAO.md` (seção 8; devolução passou a seção 9),
  `docs/INSTALACAO-SERVIDOR.md` (§3.4c: criar `Puxada` e `PuxadaTSP`, compartilhamento herdado),
  com `Pastas:Puxada`, `Pastas:PuxadaTsp`, `Pastas:AceitarRaiz`, `Zip:MaxArquivos`, `Zip:MaxTamanhoMb`.
- Manuais do usuário e técnico (ZIPs em Puxada/PuxadaTSP, raiz como puxada, Tipo e Origem no relatório,
  `ZipInvalido`), sem devolução/I40; Manual do Agente de IA com o mapa novo.
- `docs/ENCERRAMENTO.md`, `docs/diario/2026-11-12.md`, `docs/ROTEIRO-TESTE-PUXADA-TSP.md`,
  `docs/ESCOPO-PUXADA-TSP.md` (status).
- Esforço: `CONTROLE-ESFORCO.md`, `controle/SEMANAL.md`, `controle/MENSAL.md` (11/11 = 2,0 h humanas +
  3,5 h IA aprox.; 12/11 = aprox. 2,5 h IA, horas humanas a informar). Só horas.
- Deck: edições cirúrgicas com `str_replace` (slide 8: linhas 11/11 e 12/11; slide 12: Puxada TSP em
  andamento e próximos passos; "Mais de 580 testes"). "Aprox." mantido, sem "~".

## Verificações
- `dotnet test -c Release`: 584 aprovados, 9 ignorados, 0 falhas.
- Manuais regenerados com `gerar-manuais.ps1` (sem erro) e PDFs pelo Edge headless: usuário 7 páginas,
  técnico 15 páginas, datas novas. HTML dos manuais sem U+FFFD. Busca por "I40|devolu" nos `.md` dos
  manuais: só falso positivo ("QtdeVolumeZero" da I60, já existia).
- Screenshots 1280x720 (cópias temporárias com CSS injetado, apagadas depois): slide 8 (data-i=7) com
  10 linhas na tabela e nota de rodapé, nada truncado, rodapé visível; slide 12 (data-i=11) com as três
  caixas completas, nada truncado. Não foi preciso compactar mais.
- `apresentacao.pdf` regenerado pelo Edge: 12 páginas. `apresentacao.html`: BOM presente (EF BB BF),
  0 U+FFFD.
- Nos 12 documentos tocados: 0 U+FFFD; busca por `R$|US$|valor/hora|câmbio` só acha a frase da regra
  em `CONTROLE-ESFORCO.md` (preexistente, proíbe valores).
- `git ls-files`: nenhum `.xml`/`.zip` versionado.
- Sem push. Limitação: não validei a fidelidade visual dos PDFs dos manuais página a página (só contagem
  de páginas); os links do índice dos HTML dos manuais não têm `id` nos títulos (comportamento
  anterior do gerador).
