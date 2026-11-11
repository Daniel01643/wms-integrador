# Roteiro de teste da I40 (devolução) no HOMOLOG

Objetivo original: enviar a mesma nota em duas variantes (partes não invertidas e invertidas) e
decidir o default de `Devolucao:InverterPartes`.

> **Status (10/11/2026): teste feito.** A variante não invertida (`InverterPartes=false`) com
> `tipoOperacaoNota=IMP` e sem placa retornou "Registrado com sucesso" (transação 4042207) e o
> recebimento foi concluído no WMS. A variante invertida **não é necessária** e as seções 3 a 7
> ficam como histórico/consulta. Os resultados completos estão na seção 13 de
> `docs/ARQUITETURA-PLANO-B-I40.md`. As seções 9 e 10 abaixo ensinam a testar a **tela** (fase 2)
> e o modo manual atualizado (chave ou número + série; **sem placa**, que foi removida em 10/11/2026).

## Avisos antes de começar

- **Use somente credenciais e URL de HOMOLOG.** Não use usuário, senha nem URL de produção.
  O `appsettings.json` do Worker traz uma `Wms:BaseUrl` padrão; por isso o roteiro força
  `WMS__BaseUrl` explicitamente para o homolog. Confira o valor antes de executar.
- O comando envia SOMENTE a I40 (uma nota por execução). Não toca em pastas, não move
  arquivos e não depende de `Devolucao:Habilitada`.
- Placa e motorista não existem mais: `placaVeiculoPrevistoNota` e `nomeMotorista` vão sempre em branco.
- Nenhum comando abaixo imprime a senha. Não cole senha em arquivos nem no chat.
- Rode no PowerShell, na mesma sessão (as variáveis valem só para ela).

## 1. Nota candidata

Notas já integradas no homolog, com XML em `C:\wms-teste\entrada\Processados\pacote_*\`.
Exemplo:

- Nota 15671241, chave `35251201838723009850550060156712411843547443`
  (`...\Processados\pacote_20261110_095349\NFE35251201838723009850550060156712411843547443.XML`)

Outras notas candidatas do homolog (seção 9):

- Nota 15671289, **série 6**, chave `35251201838723009850550060156712891146443917`: nota nova para os cenários de busca por número e série.
- Nota 15671241, série 6, chave `35251201838723009850550060156712411843547443`: **já foi devolvida antes**; serve para ver a mensagem de duplicidade do WMS.

A I40 sai sempre como IMP, sem placa nem motorista (o `--placa` do modo manual foi removido).

## 2. Preparar a sessão (uma vez)

```powershell
# Credenciais do HOMOLOG (a senha não é exibida nem gravada)
$env:WMS__Usuario = Read-Host 'Usuario WMS (HOMOLOG)'
$sec = Read-Host 'Senha WMS (HOMOLOG)' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
try { $env:WMS__Senha = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }

# URL do HOMOLOG (porta 8888). A de producao NAO tem a porta :8888.
$env:WMS__BaseUrl = 'https://friozem.assistsoftware.com.br:8888/wmsapi/api'

# Pasta onde estao os XMLs de teste (o padrao do appsettings aponta para outra pasta)
$env:Pastas__Entrada = 'C:\wms-teste\entrada'
```

Os demais valores vêm do `appsettings.json` (`Operacional`, `Devolucao`). Por padrão a I40 sai
com `tipoNota=DE`, `tipoOperacaoNota=IMP` (sem placa nem motorista), `formaTransporte=PR`
e `codSituacaoTributaria=040`.

## 3. Variante A: partes NÃO invertidas (emitente = nós, destinatário = cliente)

```powershell
$env:Devolucao__InverterPartes = 'false'
dotnet run --project c:\wms-integrador\src\KitWmsIntegrador.Worker -c Release -- --enviar-i40 --chave 35251201838723009850550060156712411843547443
```

## 4. Variante B: partes INVERTIDAS (emitente = cliente, destinatário = nós)

Use a mesma chave. Se o WMS homolog rejeitar a nota repetida por duplicidade,
use outra nota candidata para a variante B e anote qual foi usada.

```powershell
$env:Devolucao__InverterPartes = 'true'
dotnet run --project c:\wms-integrador\src\KitWmsIntegrador.Worker -c Release -- --enviar-i40 --chave 35251201838723009850550060156712411843547443
```

Opcional: `--xml <caminho>` usa um XML específico em vez de procurar pela chave.

## 5. Como ler o resultado

No console aparecem o JSON enviado, uma linha resumo (`nota`, `tipoNota`, `tipoOperacaoNota`,
`inverter`, `emitente`, `destinatario`, `qtdeVolume`, `itens`, `destino`) e a resposta do WMS
(`transacao`, `sucesso` e, por documento, `mensagem`).

Cada execução também grava um arquivo em:

```
c:\wms-integrador\src\KitWmsIntegrador.Worker\bin\Release\net8.0\envio-manual-i40\i40_<nota>_<data>_<hora>.txt
```

Ele traz o XML usado, o destino, o JSON enviado e a resposta. Esse arquivo é a evidência do
teste. Códigos de saída: 0 = enviado e resposta lida; 2 = parâmetro ou configuração faltando
(chave ou número/série inválidos, credencial, XML não encontrado, número + série com mais de uma chave); 1 = erro no envio.

Pontos a observar na resposta:

- Se o WMS recusar o **nome da interface** (`WEBAPI-I40-Json`), o nome precisa ser corrigido.
  Ele ainda não foi confirmado.
- Se o WMS recusar o **formato de `ITEM_NOTA_FISCAL`**, registre a mensagem. Hoje ele é enviado
  como array (ver `ARQUITETURA-PLANO-B-I40.md`).

## 6. Tabela de respostas (preencher)

| Item | Variante A (`InverterPartes=false`) | Variante B (`InverterPartes=true`) |
|---|---|---|
| Nota / chave usada | | |
| Arquivo gerado em `envio-manual-i40` | | |
| Sucesso (sim/não) | | |
| Mensagem do WMS (texto) | | |
| Recebimento feito no WMS do início ao fim? | | |
| Nota apareceu no WMS com emitente/destinatário corretos? | | |
| Observações (nome da interface, array de itens) | | |

## 7. Como a resposta decide o default

| Resultado | Decisão para `Devolucao:InverterPartes` |
|---|---|
| Só a variante B aceita | Default `true`. |
| Só a variante A aceita | Default `false` (já é o atual). |
| As duas aceitas | Escolher a que representa o fluxo físico correto (cliente remete, nós recebemos) conforme o resultado no WMS (chegada e descarga corretas); em dúvida, validar com o fornecedor. |
| Nenhuma aceita | Ajustar conforme a mensagem (nome da interface, itens) e repetir. Não definir default ainda. |

Depois de decidido, o default é ajustado em `appsettings.json` (`Devolucao:InverterPartes`) e
nos testes do construtor; a mudança entra em commit próprio.

## 8. Limpeza da sessão

```powershell
Remove-Item Env:WMS__Usuario, Env:WMS__Senha, Env:WMS__BaseUrl, Env:Pastas__Entrada, Env:Devolucao__InverterPartes -ErrorAction SilentlyContinue
```

## 9. Testar a TELA de devolução no homolog (nesta máquina)

A tela roda dentro do Worker, só quando `Devolucao:Habilitada=true`. **Não ligue isso no servidor
nem em produção para testar**: use esta máquina e o WMS de HOMOLOG. Os envios feitos pela tela
vão de verdade ao homolog, então use notas de teste.

1. Confirme que os XMLs de teste estão em `C:\wms-teste\entrada\Processados\pacote_*\`.
2. Na mesma sessão do PowerShell, prepare as variáveis (a senha não é exibida nem gravada):

```powershell
$env:WMS__Usuario = Read-Host 'Usuario WMS (HOMOLOG)'
$sec = Read-Host 'Senha WMS (HOMOLOG)' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
try { $env:WMS__Senha = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }

$env:WMS__BaseUrl = 'https://friozem.assistsoftware.com.br:8888/wmsapi/api'   # HOMOLOG
$env:Pastas__Entrada = 'C:\wms-teste\entrada'
$env:Devolucao__Habilitada = 'true'
$env:Devolucao__Porta = '8085'                      # ou outra porta livre
```

3. Suba o Worker (ele também processa a pasta de entrada, então deixe `C:\wms-teste\entrada` sem
   XMLs soltos na raiz; os já processados em `Processados` não são mexidos):

```powershell
dotnet run --project c:\wms-integrador\src\KitWmsIntegrador.Worker -c Release
```

4. Abra `http://localhost:8085/` (ou a porta escolhida). O log mostra "Tela de devolução ligada na porta ...".
5. Cenários a testar (sem placa; a tela não tem mais esse campo):
   - **Busca por chave:** cole a chave `35251201838723009850550060156712891146443917`, Adicionar. Esperado:
     a lista mostra "Nota 15671289, série 6". Clique em Enviar. Esperado: "Enviada com sucesso" (IMP).
   - **Busca por número e série:** em "Ou procure pelo número e pela série" digite número `15671289` e
     série `6` (ou `006`) e clique em "Buscar e adicionar" (ou tecle Enter). Esperado: a chave
     `35251201838723009850550060156712891146443917` entra na lista, com a mensagem "Nota 15671289, série 6
     adicionada à lista." Nada é enviado até clicar em Enviar.
   - **Várias opções:** só acontece se houver duas chaves distintas com o mesmo número e série na
     retenção (por exemplo, uma nota reemitida). A tela mostra a tabela "Chaves encontradas" com chave,
     data do arquivo e pacote, e o botão "Usar esta chave" em cada linha. Esperado: nada entra na lista
     até escolher. Se não houver caso real no homolog, esse cenário está coberto pelos testes
     automatizados; anote "não reproduzido".
   - **Nota não encontrada:** número `999999` série `6`. Esperado: "Nota não encontrada na retenção de 60 dias."
   - **Entrada inválida:** número com letras ou mais de 9 dígitos, série com mais de 3 dígitos. Esperado:
     mensagem de erro, sem consultar a retenção.
   - **Duplicidade:** use a nota 15671241 (série 6, chave `35251201838723009850550060156712411843547443`),
     que já foi devolvida antes. Esperado: a tela mostra o que o WMS responder (registre o texto da mensagem
     de duplicidade).
   - **Chave inválida** (mude um dígito): "Chave inválida", sem tentar enviar.
   - **Chave sem XML** (chave válida de outra nota): "XML não encontrado (retenção de 60 dias)".
   - **Duplicada na lista:** colar a mesma chave duas vezes, ou adicionar por chave e depois por número e
     série, mantém só uma.
   - **Teclado e leitor:** digitar a chave e teclar Enter adiciona; nos campos de número e série o Enter
     busca; Tab alcança todos os campos e botões, com foco visível.
   - **Sem placa:** confirme que não há checkbox nem campo de placa e que o JSON enviado (modo manual,
     seção 10) traz `placaVeiculoPrevistoNota` e `nomeMotorista` em branco.
6. Confira a auditoria: `<Estado>\devolucao\devolucao_<data>.log` (uma linha por chave, sem senha).
7. Encerre com Ctrl+C e faça a limpeza da seção 8, incluindo:

```powershell
Remove-Item Env:Devolucao__Habilitada, Env:Devolucao__Porta -ErrorAction SilentlyContinue
```

## 10. Modo manual atualizado

Por chave: `--enviar-i40 --chave <44 dígitos> [--xml <caminho>]`.

Por número e série: `--enviar-i40 --numero <n> --serie <s> [--xml <caminho>]`. A busca usa a mesma regra
da tela (retenção, CNPJ do depositante, zeros à esquerda ignorados). Se houver mais de uma chave
distinta, o modo manual lista as chaves e **não envia**; rode de novo com `--chave`.

A I40 vai sempre como IMP, sem placa nem motorista. O parâmetro `--placa` foi removido.

Exemplo (nota nova do homolog):

```powershell
dotnet run --project c:\wms-integrador\src\KitWmsIntegrador.Worker -c Release -- --enviar-i40 --numero 15671289 --serie 6
```