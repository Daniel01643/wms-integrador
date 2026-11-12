# Roteiro de teste da Puxada e da Puxada TSP no HOMOLOG

Objetivo: confirmar, com ZIPs reais, que a `Puxada` sobe como `PUXADA` (onda por transporte), que a
`PuxadaTSP` sobe como `TSP` (uma onda por ZIP, `TSP-AAAAMMDD-NN-NOME`) e que o relatório mostra Tipo,
Origem e a onda. Escopo: [`ESCOPO-PUXADA-TSP.md`](ESCOPO-PUXADA-TSP.md). Regras de funcionamento:
[`OPERACAO.md`](OPERACAO.md), seção 8.

Estimativa deste teste: horas humanas a informar pelo usuário (conferência no WMS e no relatório);
horas de IA, apenas se for preciso apoio na análise do resultado.

## Avisos antes de começar

- **Use somente credenciais e URL de HOMOLOG** (porta `:8888`). A de produção não tem a porta.
- O serviço envia as notas de verdade ao WMS de homolog. Notas que já foram integradas voltam como
  "já existe" (reprocesso); para um teste limpo, use notas ainda não enviadas ou aceite essa mensagem.
- **Não versione ZIPs nem XMLs.** Os ZIPs reais ficam só em `C:\wms-teste` (fora do repositório).
- Nenhum comando abaixo imprime a senha. Não cole senha em arquivos nem no chat.
- Rode no PowerShell, na mesma sessão (as variáveis valem só para ela).

## 1. Preparar a pasta de teste

```powershell
$e = 'C:\wms-teste\entrada'
New-Item -ItemType Directory -Force -Path "$e\Puxada","$e\PuxadaTSP" | Out-Null
Get-ChildItem $e -Directory | Select-Object Name
```

## 2. Colocar um ZIP em cada pasta

Use um ZIP de puxada normal em `Puxada` e um ZIP de TSP em `PuxadaTSP` (com nomes diferentes, por
exemplo `XML'S SJC.zip` na TSP). Para repetir o teste do começo, use ZIPs com notas ainda não enviadas.

```powershell
Copy-Item "C:\wms-teste\<zip-da-puxada>.zip" "$e\Puxada\"
Copy-Item "C:\wms-teste\XML'S SJC.zip" "$e\PuxadaTSP\"
```

## 3. Iniciar o serviço apontando para a pasta de teste

```powershell
$env:WMS__Usuario = Read-Host 'Usuario WMS (HOMOLOG)'
$sec = Read-Host 'Senha WMS (HOMOLOG)' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
try { $env:WMS__Senha = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
$env:WMS__BaseUrl = 'https://friozem.assistsoftware.com.br:8888/wmsapi/api'
$env:Pastas__Entrada = 'C:\wms-teste\entrada'
dotnet run --project c:\wms-integrador\src\KitWmsIntegrador.Worker -c Release
```

Se o e-mail estiver desligado (`Email:Habilitado=false`), o relatório fica salvo em disco. Depois do
fechamento do pacote (janela de coleta, processamento e relatório), pare com `Ctrl+C`.

## 4. O que conferir no relatório

Abra o HTML mais recente em `C:\wms-teste\relatorios` (`pacote_<data_hora>.html`).

- [ ] Colunas **Tipo** e **Origem** presentes.
- [ ] Notas do ZIP da `Puxada`: Tipo `PUXADA`, Origem `Puxada`, agrupadas **por transporte**.
- [ ] Notas do ZIP da `PuxadaTSP`: Tipo `TSP`, Origem `PuxadaTSP`, agrupadas **por onda**
      (ex.: `TSP-<data>-01-SJC`), com a quantidade de notas e de transportes; a coluna Transporte
      mostra o transporte de cada nota.
- [ ] O resumo mostra as notas por tipo; "Notas da raiz" só aparece se houver XML solto na raiz.
- [ ] Nenhum arquivo sobrou nas pastas `Puxada` e `PuxadaTSP`; os XMLs e o ZIP estão em
      `Processados\pacote_*\` (ou o ZIP em `Erro\pacote_*\ZipInvalido`, com `.motivo.txt`).

## 5. O que conferir no WMS

- [ ] A TSP criou **uma única onda** (`nroagrupseparacao` = `TSP-<data>-NN-<NOME>`) com todas as notas
      do ZIP, e um DocCarga por nota.
- [ ] A puxada normal criou uma onda por transporte, como antes.
- [ ] **Premissa a validar:** o WMS aceitou o `numeroPlanejamento` igual ao nome da onda na TSP.
- [ ] A separação por onda única funciona (os itens de todas as notas do ZIP na mesma onda).

## 6. Casos extras (rápidos)

- **Mesmo ZIP de novo na `PuxadaTSP`:** deve reaproveitar a mesma onda (sem novo `NN`); as notas
  voltam como "já existe".
- **ZIP diferente no mesmo dia:** a onda seguinte ganha `NN` = `02`.
- **XML solto na raiz:** sobe como `PUXADA`, Origem `raiz`, e aparece no relatório.
- **Arquivo não-ZIP na `PuxadaTSP`:** vai para `Erro\pacote_*\ExtensaoInvalida`.

## 7. Registrar o resultado

Anote no diário do dia o que passou e o que falhou, a onda criada e as horas humanas gastas (informadas
pelo usuário). Se algo falhar, guarde o `pacote_*.html` e o trecho de `logs\worker.out.log` (sem segredos).
