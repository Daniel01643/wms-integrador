# Roteiro de teste no homolog: I51 como gatilho da I60

Status: **em execução no homolog desde 13/11/2026** (proprietária BRF liberada pelo fornecedor). Resultados na seção 7.
Tudo aqui foi coberto por testes automatizados com WMS fake; o que muda no WMS real está marcado como "registrar".
Não colar tokens, senhas nem corpos de nota nos registros do teste.

## 0. Pré-requisitos

- EDI da I51 criado no WMS (meio de envio e retorno = WEBSERVI) com o identificador final; ajustar `I51__Identificador` se não for `WEBAPI-I51`.
- Credenciais só por ambiente: `WMS__Usuario` e `WMS__Senha`.
- Chaves **desligadas** por padrão. No teste, ligar por variável de ambiente (não editar o appsettings):
  `I51__Habilitada=true`, `I60__Habilitada=true`. Opcional: `I51__IntervaloMinutos=1`.
- Worker parado antes de mudar variáveis; reiniciar depois.
- **Aviso:** a primeira ativação do TODOS consome o backlog da proprietária (o WMS não devolve de novo o que já foi puxado).
  Os brutos ficam em `Estado\i51\brutos\processados\<data>`. Se aparecerem documentos que não são nossos, anotar `Tipo`/`SubTipo`.

## 1. UNICO manual (não consome nada)

`KitWmsIntegrador.Worker.exe --consultar-i51 --tipo UNICO --cnpj 01838723009850 --numero <nNF>`

- Testar as **duas variantes** do campo: `I51__CampoUnico=Numero` (padrão, 9 dígitos) e `I51__CampoUnico=NroDocumento`. Registrar qual o WMS aceita (Q13).
- Registrar a mensagem real do WMS quando não há nada (`sucesso=N`) e quando o EDI/CNPJ está errado; preencher `I51__PadroesSemRetorno` (Q14).
- `--tipo TODOS` é recusado de propósito (saída 2).

## 2. Ciclo completo

1. Enviar um pacote (Puxada) com 1 nota; conferir I50 aceita e `Estado\i51\notas\<chave>.json` em `AguardandoSeparacao`; a I60 **não** sai junto (histórico: "I60 adiada").
2. Concluir a separação no WMS. Em até `IntervaloMinutos` o log traz `Ciclo I51: ... novasSeparadas=1`.
3. Conferir a I60 enviada: operação `INE`, quantidades iguais às separadas, mesma NF-e. Registrar a resposta real (sucesso? pendente na interface?) e se `Associada` é o desfecho certo (Q11, `I60__PadroesAssociadaSucesso`).
4. **Contar órfãos** (`Estado\i51\orfaos`) após o primeiro TODOS: se houver órfãos e nenhuma nota casou, a premissa "Numero da I51 = nNF" está errada.

## 3. Casos

- **Quantidade diferente:** separar menos que o pedido; conferir no estado original × separada e que a I60 saiu ajustada. Se o WMS recusar, registrar o texto e ajustar `I60__PadroesQuantidadeDiferente`.
- **Volume:** registrar se a conferência de volume passa com `I60__QtdeVolumeOrigem=SomaCaixas` ou exige `VolumesI51` (Q12).
- **ENC (doccarga encerrado):** com um doccarga encerrado, registrar o texto exato da resposta e ajustar `I60__PadroesDocCargaEncerrado`. Esperado: reenvio como `IMP`, nota em `I60Enviada` com motivo `DocCargaEncerrado`, nunca `Associada`. Registrar se o reenvio precisa de `TpAcao=UPD` (padrão) ou `I` (`I60__TpAcaoReenvioImp`) (Q1, Q2).
- **Já existe / UPD:** reenviar uma nota já importada; esperado 1 reenvio UPD.
- **Onda TSP:** pacote com ZIP em `PuxadaTSP`; o estado traz `Tipo=TSP` e `Onda`.
- **Reinício:** parar o Worker entre a I50 e a separação e entre a separação e a I60; ao voltar, o estado é retomado sem reenviar a I50.
- **Atrasada:** com `I51__HorasParaAtraso=1`, aguardar; a nota vira `Atrasada` e o UNICO de recuperação a consulta.
- **Reenfileirar:** após ajustar padrões, `Worker.exe --reenfileirar-i60 --motivo Outro` (ou `--chave <chave>`). O pedido vai para `Estado\i51\comandos`; o Worker em execução aplica no ciclo seguinte (não precisa reiniciar).

## 4. Relatório diário

`Worker.exe --relatorio-i60 [--enviar]` gera HTML e PDF em `Relatorio:PastaSaida\i60` sem esperar as 12:00. Conferir seções, idades, ajustes, visão por pacote e a lista de candidatas à devolução pela I40.

## 5. Regressão da tela I40

Repetir `ROTEIRO-TESTE-I40.md` seção 9 com `Devolucao__Habilitada=true` e o fluxo I51 ligado. A I40 continua última alternativa, fora dos manuais oficiais.

## 6. Perguntas a fechar

Q1 texto do ENC; Q2 TpAcao do IMP; Q11 sinal de associada; Q12 volume; Q13 campo do UNICO; Q14 mensagem do `sucesso=N`.

## 7. Resultados no homolog (13/11/2026)

Pacote `pacote_20261113_152914` (Puxada TSP, 2 ZIPs, 7 notas): ondas `TSP-20261112-01-SOROCABA` (5 notas) e
`TSP-20261112-02-SJC` (2 notas). I51 e I60 ligadas por variável de ambiente; `I51__IntervaloMinutos=1`.

| Passo | Resultado |
|---|---|
| I50 com I51 ligada | 7 notas registradas em `AguardandoSeparacao`; I60 não saiu junto |
| 1ª consulta TODOS | Falhou (EDI da I51 ainda não configurado no WMS). Após configurar, OK sem reiniciar |
| `sucesso=N` sem separação (Q14) | "Nenhum Documento encontrado com as especificacoes passadas ou ja consultados" |
| Backlog do 1º TODOS | Vazio: nenhum órfão |
| Separação da onda SJC | TODOS trouxe as 2 notas; `Numero` da I51 = nNF (casamento OK, Q13: `Numero`) |
| 1ª I60 (INE, `qtdeVolume`=1) | Recusada: "Total de volume(s) do(s) pedido(s)(doccarga):0 , não confere com a qtdvolume da nota venda :1" → `Rejeitada/VolumeDiferente` |
| Ajuste (Q12) | `I60__QtdeVolumeZero=true` e `I60__QtdeTotalNotaZero=true` (nova opção, `qtdeTotalNota = 0`) |
| Reenfileirar + reenvio (INE, `TpAcao=I`) | **"Registrado com sucesso" → `Associada`** (16812958 tx 4050226, 16812959 tx 4050227). Associação conferida no WMS pelo usuário. Não precisou de UPD |

Atenção: a CLI `--reenfileirar-i60` usa a mesma configuração do Worker. Rodada num terminal sem
`Pastas__Entrada`, gravou o pedido em `C:\Envia xml\Estado\i51\comandos` (pasta padrão) e o Worker não o viu.
Defina as mesmas variáveis no segundo terminal.

Pendentes: onda SOROCABA (5 notas, 4 transportes), quantidade diferente, ENC → IMP, atrasada, reinício,
relatório diário (`--relatorio-i60`), regressão da I40 com login.
