# Relatório: ajuste do deck em 16/11/2026

Arquivo: `docs/apresentacao/apresentacao.html` (14 slides, BOM preservado, edições pontuais) e `apresentacao.pdf` regenerado.

## O que mudou por slide
- 3: teto da coleta escrito como 180 s (janela de silêncio de 20 s já estava correta).
- 4: linha do 401 com token por envio, nova tentativa em conexão nova e alerta de queda e de recuperação.
- 6: devolução (I40) no ar em produção desde 16/11.
- 7: chaves I51/I60 ligadas em produção em 16/11 (e como desligar); Devolucao:Habilitada ligada na porta 8085; situação com homolog 13/11 (7 de 7, qtdeVolume e qtdeTotalNota = 0, ajuste de quantidade aceito), ligação em produção e ENC ainda desconhecido.
- 11: GetToken (token por envio desde a release -3), retenção 30 dias (era 10), nota de rodapé com a causa do 401 confirmada pelo fornecedor.
- 14: reescrito como "Estado em 16/11/2026: projeto finalizado" (classe CSS nova `compact-fim`): release 2026.11.16-3, Puxada com placa e TSP sem placa (regras de ZIP/XML), tipoSeparacao PIC;FEF;PLL e AGL;FEF;PLL, silêncio 20 s/teto 180 s e 1º lote de 578 notas, retenção 30 dias, I51→I60 ligada, tela de devolução (links, login simples, rede interna, firewall Domínio), causa do 401 confirmada pelo fornecedor (3 servidores, token por envio, usuário BRF, sem senha no deck), 383 notas NÃO reprocessadas, projeto travado e pendências fora do escopo.
- Slides de horas (8, 12 e 13) e números de horas: não alterados. O rodapé do slide 7 sobre horas ficou como estava.

## Verificação
- BOM EF BB BF presente após as edições; fins de linha LF mantidos.
- grep por `60 dias`, `10 s`, `perguntas`, `desligad`, `a publicar`, `10 dias`: sem ocorrências desatualizadas. Senhas: nenhuma no deck.
- Capturas em `.agents/tasks/shots-deck-1611/` (slide3, 4, 6, 7, 11, 11b, 14): sem texto estourado, rodapés inteiros. O slide 11 truncava o rodapé numa primeira versão e foi encurtado.
- PDF regenerado com o Edge headless (19:18).

## Horas de IA
Aprox. 0,3 h (sessão de ajuste do deck). Horas humanas: não informadas aqui.

