# Roteiro de teste da I40 (devolução) no HOMOLOG

Objetivo: enviar a mesma nota de devolução em duas variantes (partes não invertidas e
invertidas) e anotar a resposta do WMS. A resposta decide o default de `Devolucao:InverterPartes`
(ver seção 5).

## Avisos antes de começar

- **Use somente credenciais e URL de HOMOLOG.** Não use usuário, senha nem URL de produção.
  O `appsettings.json` do Worker traz uma `Wms:BaseUrl` padrão; por isso o roteiro força
  `WMS__BaseUrl` explicitamente para o homolog. Confira o valor antes de executar.
- O comando envia SOMENTE a I40 (uma nota por execução). Não toca em pastas, não move
  arquivos e não depende de `Devolucao:Habilitada`.
- O motorista (`nomeMotorista`) precisa estar **cadastrado no WMS homolog** com o nome exato.
- Nenhum comando abaixo imprime a senha. Não cole senha em arquivos nem no chat.
- Rode no PowerShell, na mesma sessão (as variáveis valem só para ela).

## 1. Nota candidata

Notas já integradas no homolog, com XML em `C:\wms-teste\entrada\Processados\pacote_*\`.
Exemplo:

- Nota 15671241, chave `35251201838723009850550060156712411843547443`
  (`...\Processados\pacote_20261110_095349\NFE35251201838723009850550060156712411843547443.XML`)

Placa: use uma placa de teste válida (ex.: `ABC1D23`; informe a que o WMS homolog aceitar).

## 2. Preparar a sessão (uma vez)

```powershell
# Credenciais do HOMOLOG (a senha não é exibida nem gravada)
$env:WMS__Usuario = Read-Host 'Usuario WMS (HOMOLOG)'
$sec = Read-Host 'Senha WMS (HOMOLOG)' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
try { $env:WMS__Senha = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }

# URL do HOMOLOG (preencha com a URL de homolog; NAO a de producao)
$env:WMS__BaseUrl = '<url-base-da-api-do-homolog>'

# Pasta onde estao os XMLs de teste (o padrao do appsettings aponta para outra pasta)
$env:Pastas__Entrada = 'C:\wms-teste\entrada'

# Motorista fixo cadastrado no WMS homolog
$env:Devolucao__NomeMotorista = '<nome cadastrado>'
```

Os demais valores vêm do `appsettings.json` (`Operacional`, `Devolucao`). Por padrão a I40 sai
com `tipoNota=DE`, `tipoOperacaoNota=ICD`, `formaTransporte=PR`, `codSituacaoTributaria=040`.

## 3. Variante A: partes NÃO invertidas (emitente = nós, destinatário = cliente)

```powershell
$env:Devolucao__InverterPartes = 'false'
dotnet run --project c:\wms-integrador\src\KitWmsIntegrador.Worker -c Release -- --enviar-i40 --chave 35251201838723009850550060156712411843547443 --placa ABC1D23
```

## 4. Variante B: partes INVERTIDAS (emitente = cliente, destinatário = nós)

Use a mesma chave e a mesma placa. Se o WMS homolog rejeitar a nota repetida por duplicidade,
use outra nota candidata para a variante B e anote qual foi usada.

```powershell
$env:Devolucao__InverterPartes = 'true'
dotnet run --project c:\wms-integrador\src\KitWmsIntegrador.Worker -c Release -- --enviar-i40 --chave 35251201838723009850550060156712411843547443 --placa ABC1D23
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
(chave inválida, placa, credencial, XML não encontrado); 1 = erro no envio.

Pontos a observar na resposta:

- Se o WMS recusar o **nome da interface** (`WEBAPI-I40-Json`), o nome precisa ser corrigido.
  Ele ainda não foi confirmado.
- Se o WMS recusar o **formato de `ITEM_NOTA_FISCAL`**, registre a mensagem. Hoje ele é enviado
  como array (ver `ARQUITETURA-PLANO-B-I40.md`).
- Se recusar o motorista ou a placa, confira o cadastro no homolog.

## 6. Tabela de respostas (preencher)

| Item | Variante A (`InverterPartes=false`) | Variante B (`InverterPartes=true`) |
|---|---|---|
| Nota / chave usada | | |
| Arquivo gerado em `envio-manual-i40` | | |
| Sucesso (sim/não) | | |
| Mensagem do WMS (texto) | | |
| Gerou chegada de veículo / descarga (ICD)? | | |
| Nota apareceu no WMS com emitente/destinatário corretos? | | |
| Observações (nome da interface, array de itens, motorista, placa) | | |

## 7. Como a resposta decide o default

| Resultado | Decisão para `Devolucao:InverterPartes` |
|---|---|
| Só a variante B aceita | Default `true`. |
| Só a variante A aceita | Default `false` (já é o atual). |
| As duas aceitas | Escolher a que representa o fluxo físico correto (cliente remete, nós recebemos) conforme o resultado no WMS (chegada e descarga corretas); em dúvida, validar com o fornecedor. |
| Nenhuma aceita | Ajustar conforme a mensagem (nome da interface, itens, motorista, placa) e repetir. Não definir default ainda. |

Depois de decidido, o default é ajustado em `appsettings.json` (`Devolucao:InverterPartes`) e
nos testes do construtor; a mudança entra em commit próprio.

## 8. Limpeza da sessão

```powershell
Remove-Item Env:WMS__Usuario, Env:WMS__Senha, Env:WMS__BaseUrl, Env:Pastas__Entrada, Env:Devolucao__NomeMotorista, Env:Devolucao__InverterPartes -ErrorAction SilentlyContinue
```
