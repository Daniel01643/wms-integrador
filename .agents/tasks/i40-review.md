# Revisao I40: busca por chave ou numero+serie e remocao da placa

Veredito: APROVADO. Nenhum bloqueio.

Evidencia lida: `.agents/tasks/i40-verificacao-coder.md` (build 0 avisos/0 erros; testes 487 aprovados, 0 falhas, 9 ignorados; antes 451). Nada foi reexecutado.

- (a) `LocalizadorXmlDevolucao.BuscarPorNumeroSerie` le numero/serie/CNPJ do nome do arquivo (CNPJ pos. 7-20, serie 23-25, numero 26-34). Compara como inteiros, filtra pelo CNPJ do depositante so com digitos, valida DV, varre Processados e `pacote_*` (nunca Erro), usa a copia mais recente por chave e ordena do mais novo. Varias chaves viram opcoes (chave, data, pacote) sem envio. Nao achada gera 404 "Nota nao encontrada na retencao de 60 dias.".
- (b) `GET /api/devolucao/buscar` valida no servidor (so digitos, numero max 9, serie max 3) e responde 200/404/400/503/500.
- (c) Pagina: labels, `:focus-visible`, `role="alert"`, `aria-live`, limite 30 vindo de `/api/config`, sem duplicadas, lista mostra numero/serie, botao de remover com aria-label.
- (d) `git grep` por EnviarPlacaPadrao, TipoOperacaoNotaComPlaca, NomeMotorista e `--placa` so acha docs historicos e `.agents/tasks` antigos, mais notas de compatibilidade nas docs. Nada em `src/` ou `tests/`. `ConstrutorI40.Construir(nota)` sem placa. Modo manual aceita `--chave` ou `--numero --serie` e `--xml`. `tipoOperacaoNota` segue configuravel (padrao IMP). Os campos `nomeMotorista` e `placaVeiculoPrevistoNota` no DTO ficam em branco, como o layout da I40 exige. I50 nao foi tocada.
- (e) `Devolucao:Habilitada=false` e `I60:Habilitada=false` no appsettings do Worker e em `deploy/config-exemplo/appsettings.Production.json`.
- (f) `git ls-files` sem `.xml`. Sem `PackageReference` novo. Nenhum valor monetario adicionado nos docs.
- (g) Testes novos: `BuscaNumeroSerieDevolucaoTests`, `TelaDevolucaoEndpointTests`, `ValidadorChavesDevolucaoTests`, `ServicoDevolucaoTests`. Cobrem extracao, busca (uma, varias, nenhuma, outro CNPJ, zeros, Erro, mais recente), endpoint e validacao.
- (h) Docs atualizados: ARQUITETURA (secao 19), ROTEIRO secao 9, MANUAL-USUARIO, MANUAL-TECNICO, OPERACAO, INSTALACAO, ENCERRAMENTO e diario 2026-11-10.

Nao verificado: o JS da pagina nao tem teste automatizado nem foi aberto no navegador. Isso e uma limitacao declarada e nao e bloqueio.
