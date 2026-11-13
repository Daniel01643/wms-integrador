# Evidências: login simples na tela de devolução (I40)

Execução em cópia temporária (c:\wms-integrador-tmp-login), sem tocar o Worker em execução.

- `dotnet build KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test -c Release`: 889 aprovados, 0 falhas, 9 ignorados (baseline 863/9; +26 testes novos em `LoginDevolucaoTests.cs`).
- Testes novos: usuário ausente/vazio/inválido (`;`, quebra de linha, `/`, `'`, `<`, `,`, `|`) = 400; 61 caracteres = 400 e 60 = 200;
  usuário válido grava a coluna 3 da auditoria e `usuario=` no log (logger coletor); a senha (enviada no corpo de propósito) não aparece
  na resposta, na auditoria, em nenhuma mensagem de log, no payload ao WMS nem em nenhum arquivo da pasta de teste;
  validação de chaves inalterada; página tem login, nota, "Enviar devolução", "Trocar usuário", sessionStorage.
- Testes existentes ajustados: POSTs agora levam `usuario`; auditoria passou de 7 para 8 campos.
- Decisão de desenho: a página NÃO envia a senha; o servidor nem mapeia a propriedade `senha` (descartada pelo desserializador).
  Validação do usuário no endpoint (`ValidadorUsuarioDevolucao`); o serviço tolera usuário nulo (grava "-").
- Auditoria: nada no código lê os arquivos `devolucao_*.log`; linhas antigas (7 campos) seguem válidas só para consulta.
- Capturas (Edge headless 1280x1100): `.agents/tasks/login-devolucao-1.png` (sem usuário, botão desabilitado) e
  `login-devolucao-2.png` (com "Enviando como: maria.silva" e "Trocar usuário").
- Docs atualizados: ARQUITETURA-PLANO-B-I40.md (nova seção 20 com a decisão e o risco aceito), ROTEIRO-TESTE-I40.md (passo de login e linha de auditoria),
  OPERACAO.md, MANUAL-USUARIO.md (seção 11), MANUAL-TECNICO.md (8.6). HTML regenerado com `gerar-manuais.ps1` e PDFs via Edge `--no-pdf-header-footer --print-to-pdf`.
- Decisão registrada: link aberto na LAN sem restrição de firewall por decisão do usuário (13/11/2026); risco aceito: quem tem o link pode enviar; identificação autodeclarada.
- Cópia temporária removida ao final.