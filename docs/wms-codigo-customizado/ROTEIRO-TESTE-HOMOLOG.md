# Roteiro de teste em HOMOLOG: I50 + código customizado + I60

Objetivo: provar que o código customizado grava `QTD_VOLUMES` do DocCarga e que a I60, com
`qtdeVolume` igual à soma das caixas dos itens, associa ao DocCarga. Tudo em **homolog**.
Produção não é tocada neste roteiro.

## 0. Antes de começar

- O PL/SQL está em `doccarga-qtd-volumes.sql` (explicação em `README.md`, na mesma pasta).
- Não ligue a I60 no serviço de produção. A flag é ligada só por variável de ambiente, só na
  sessão do teste (item 3). O default versionado continua `I60:Habilitada=false`.
- Use uma nota **ainda não enviada** ao homolog. A nota de exemplo dos testes (nº 15665807, em
  `tests\KitWmsIntegrador.Tests\Recursos`) já existe no homolog: a resposta anterior foi
  "Nota [15665807] já existe, TpAcao precisa ser UPD" (ver `docs\chamado\respostas-homolog.txt`).
  Pegue um XML real de venda recente do KIT, com "Transporte: N" nas informações complementares.

## 1. Gravar o código customizado (você, no WMS homolog)

1. Abra Código Customizado, pré-processamento da importação do DocCarga, vinculado somente ao
   depositante da BRF (CNPJ 01.838.723/0098-50).
2. Cole o conteúdo de `doccarga-qtd-volumes.sql` e ative.
3. Confirme o nome da coluna (`QTD_VOLUMES` ou `QTDVOLUMES`). Se for o segundo, ajuste só a
   linha do `SET` (ver `README.md`).

## 2. Credenciais de homolog (sem imprimir a senha)

Neste computador, na sessão do PowerShell que vai rodar o teste:

```powershell
$env:WMS__Usuario = Read-Host "Usuario WMS homolog"
$s = Read-Host "Senha WMS homolog" -AsSecureString
$env:WMS__Senha = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
```

As variáveis valem só nesta sessão. No servidor (fase homolog), o caminho já documentado é
`.\configurar-segredos.ps1 -Fase Homolog` (ver `docs\INSTALACAO-SERVIDOR.md`, seção 3.5).

## 3. Rodar o integrador apontando para o homolog

O `appsettings.json` versionado já aponta para o homolog
(`Wms:BaseUrl = https://friozem.assistsoftware.com.br:8888/wmsapi/api`, com `:8888`).
Confirme que não há `Wms__BaseUrl` de produção na sessão (`$env:Wms__BaseUrl` deve estar vazio).

1. Crie uma pasta de teste e coloque **um** XML novo nela:

   ```powershell
   $entrada = "C:\temp\teste-i60"
   New-Item -ItemType Directory -Force $entrada | Out-Null
   Copy-Item "<caminho\do\XML-novo>.xml" $entrada
   ```

2. Ligue a I60 só nesta sessão e inicie o Worker na pasta de teste:

   ```powershell
   $env:I60__Habilitada = "true"
   $env:Pastas__Entrada = $entrada
   dotnet run --project c:\wms-integrador\src\KitWmsIntegrador.Worker -c Release
   ```

   Opcional: `$env:I60__TipoOperacaoNota = "IMB"` (ou outro valor) para testar outro tipo sem
   alterar arquivo. Sem isso vale `INE`.
3. Aguarde o pacote fechar (a janela de coleta é de 10 s; o log mostra o fechamento). Encerre com
   Ctrl+C.
4. Ao terminar, limpe a sessão para não deixar nada ligado:

   ```powershell
   Remove-Item Env:I60__Habilitada, Env:I60__TipoOperacaoNota, Env:Pastas__Entrada, Env:WMS__Usuario, Env:WMS__Senha -ErrorAction SilentlyContinue
   ```

O e-mail de fechamento está desligado no `appsettings.json` (`Email:Habilitado=false`); o
relatório HTML/PDF fica em `relatorios` ao lado da pasta de entrada.

Se preferir rodar no servidor em fase homolog, use a instalação documentada em
`docs\INSTALACAO-SERVIDOR.md` (seções 3.4 a 3.7), com `I60__Habilitada=true` somente na
instância de homolog. Não ligue essa variável na instância de produção.

## 4. O que conferir no WMS

Na tela do WMS (o código customizado roda na importação). Opcionalmente, quem tem acesso ao
banco do homolog pode rodar a query de conferência somente leitura do `README.md` desta pasta
(`diferenca` deve ser 0):

1. **DocCarga do teste**: o número do pedido é o nº da nota (nNF). Confira que o campo
   `QTD_VOLUMES` está preenchido com a soma das quantidades em caixas dos itens
   (`qtd_informada_cxs`).
2. **Resposta da I60** (log do Worker `worker.out.log`, relatório do pacote): sem a mensagem
   "Total de volume(s) do(s) pedido(s)(doccarga): 0, nao confere com a qtdvolume da nota venda".
3. **Associação**: a nota de venda aparece associada ao DocCarga (pedido) no WMS.
4. No payload da I60, `qtdeVolume` deve ser a mesma soma. O relatório do pacote e o log mostram
   o resultado por nota.

## 5. Se aparecer erro

- **Total de volumes do DocCarga 0 e a nota com volume**: o código customizado não rodou ou não
  gravou. Confirme se está ativo para o depositante e o nome da coluna.
- **Erro de conferência de caixa por item** (a quantidade em caixas do item não bate): anote a
  mensagem exata, o pedido e o item. Isso indica que a regra por item do WMS difere da soma
  simples; **não** mude a produção. Traga a mensagem para decidirmos o ajuste (vale a decisão
  "primeiro `qtd_volumes`, depois apurar os erros").
- **"Nota já existe, TpAcao precisa ser UPD"**: a nota já foi enviada antes; use outra nota.
- **Rejeição por tipo de operação**: teste outro valor em `I60__TipoOperacaoNota` (IMB, INE ou
  IMP aparecem na documentação do chamado). O manual do repositório não cita `INV`.
- **401/credencial**: confira usuário e senha de homolog na sessão.

## 6. Promover para produção

Depois que tudo estiver certo no homolog:

1. Gere a release na máquina de build e promova no servidor com `publicar-release.ps1`, como
   descrito em `docs\INSTALACAO-SERVIDOR.md`, seção 3.9 (backup, virada, reinício, rollback).
   Não repetimos os passos aqui.
2. A I60 em produção continua desligada por padrão. Só ligue com `I60__Habilitada=true` quando o
   teste de homolog estiver aprovado e você decidir, e reinicie o serviço
   (`.\servico.ps1 -Acao Restart`).
3. O código customizado precisa estar gravado também no WMS de produção (passo 1 deste roteiro,
   no ambiente de produção) antes de ligar a I60 lá.
