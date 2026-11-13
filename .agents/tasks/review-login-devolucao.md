# Revisão: login simples na tela de devolução (I40)

Veredito: APROVADO. Nenhum bloqueio.

Base: commit local 6c6b505 (único à frente de origin/main, sem push). Evidência lida em `evidencias-login-devolucao.md` e captura `login-devolucao-2.png`. Build e testes não foram reexecutados, conforme instrução.

## Verificações

- Validação do usuário (`ValidadorUsuarioDevolucao`): trim, 1..60 caracteres e regex `[\p{L}\p{N}._@ \-]`. O endpoint devolve 400 com mensagem em pt-BR antes de chamar o serviço. Como o valor é aparado, o `$` da regex não deixa passar uma quebra de linha final. Chaves e validação antigas não foram alteradas.
- Senha: `EnvioDevolucaoRequest` não mapeia `senha`, então o desserializador a descarta. A página também não a envia (o body leva só `usuario` e `chaves`), e `sessionStorage` guarda só o usuário. O serviço e a chamada ao WMS não recebem senha, e o WMS segue com a conta de serviço. A evidência cita testes que enviam a senha no body e conferem que ela não aparece em resposta, auditoria, log, payload ao WMS nem arquivos.
- Auditoria: o formato passou a `data;ip;usuario;chave;nota;status;transacao;mensagem`. O usuário é sanitizado (`;` vira `,`, quebras de linha viram espaço, limite de 60). O log da aplicação traz `usuario=` sem senha. Nada no código lê os `devolucao_*.log`, então linhas antigas de 7 campos só servem para consulta. Isso está documentado e é aceitável.
- Página: há o cartão de login com a nota exata pedida. Enviar fica desabilitado sem usuário, aparece "Enviando como: ..." e há "Trocar usuário". Rótulos, `aria-describedby`, `role=alert` e `aria-live` estão presentes, e a captura confirma o resultado.
- Docs: ARQUITETURA (nova seção), ROTEIRO, OPERACAO, MANUAL-USUARIO, MANUAL-TECNICO, HTML e PDFs foram atualizados, com a decisão de 13/11/2026 e o risco aceito. Não encontrei valores monetários.
- Higiene: o commit não inclui `*.xml`, `*.zip`, dados reais, `docs/CONTROLE-ESFORCO.md`, `docs/controle/*` nem `docs/diario/*`.

## Observação (não bloqueante)

O working tree tem mudanças não commitadas em `OpcoesI60.cs`, `ConstrutorI60.cs` e `ConstrutorI60Tests.cs`. Elas são de outra tarefa (I60) e ficaram fora desta revisão.
