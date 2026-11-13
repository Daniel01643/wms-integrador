# Relatório: login simples na tela de devolução (I40)

Data: 13/11/2026. Commit local 6c6b505 (sem push).

## O que foi pedido

Link da tela de notas aberto na rede, com uma tela de login "falsa": o usuário informa o login do WMS (pode digitar a senha, mas ela é ignorada). O login fica registrado nos registros de envio, para sabermos quem enviou.

## O que mudou

- Página: cartão de login com a nota de que a identificação é autodeclarada. "Enviar devolução" fica desabilitado sem usuário. Mostra "Enviando como: ..." e tem "Trocar usuário". O usuário fica só no `sessionStorage`.
- API: o POST de devolução exige `usuario`. A validação (`ValidadorUsuarioDevolucao`) aceita de 1 a 60 caracteres, com letras, números e `. _ @ -` e espaço. Valor inválido devolve 400 em pt-BR. A senha não é lida nem enviada: a página não a manda e o servidor não mapeia a propriedade. O WMS segue com a conta de serviço.
- Auditoria: o formato passou a `data;ip;usuario;chave;nota;status;transacao;mensagem` (8 campos, antes 7). O usuário é sanitizado (`;` vira `,`, quebra de linha vira espaço, limite de 60). Nada no código lê os `devolucao_*.log`, então linhas antigas servem só para consulta.
- Log da aplicação: a linha de envio passou a incluir `usuario=`, sem senha.
- Testes: 26 novos em `LoginDevolucaoTests.cs`. Testes existentes ajustados (POSTs levam `usuario`; auditoria com 8 campos).
- Docs: ARQUITETURA-PLANO-B-I40 (seção 20), ROTEIRO-TESTE-I40, OPERACAO, MANUAL-USUARIO (seção 11) e MANUAL-TECNICO (8.6). HTML regenerado e PDFs gerados.

## Build e testes

Executados em cópia temporária, sem tocar o Worker em execução (cópia removida ao final).

- `dotnet build KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test -c Release`: 889 aprovados, 0 falhas, 9 ignorados (baseline 863/9).
- Capturas de tela: `login-devolucao-1.png` e `login-devolucao-2.png`.
- Revisão: APROVADO, sem bloqueios. Build e testes não foram reexecutados na revisão.
- Observação da revisão (não bloqueante): havia mudanças não commitadas de outra tarefa (I60) no working tree, fora desta entrega. Elas foram commitadas depois, à parte, no commit 4137346.

## Decisão de 13/11/2026 e risco aceito

Por decisão do usuário, o link fica aberto na LAN, sem restrição de firewall, pois o uso será pequeno. Risco aceito: quem tiver o link consegue enviar, e a identificação é autodeclarada, então não prova quem enviou. Isso está registrado na seção 20 da arquitetura.

## Esforço (somente horas)

- Horas humanas: a informar pelo usuário.
- Horas de IA: cerca de 0,7 h (estimativa). Base: o tempo de sessão dos passos do workflow (implementação, testes e docs até a evidência às 16:15; revisão até 16:17; este relatório). Não tive os horários de início dos passos, então o valor é aproximado e deve ser ajustado se o registro do workflow indicar outro tempo.
- O registro mestre (`docs/CONTROLE-ESFORCO.md`) não foi alterado.
