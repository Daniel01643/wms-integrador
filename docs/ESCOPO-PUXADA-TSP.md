# Escopo: Puxada e Puxada TSP (ZIP por pasta)

Status: **escopo fechado em 11/11/2026; implementado e documentado em 12/11/2026** (código revisado e
aprovado; documentação e manuais atualizados). Falta o teste com ZIP real no homolog
(`ROTEIRO-TESTE-PUXADA-TSP.md`). Premissas assumidas na implementação: XML repetido no pacote é ignorado (fica em `Processados`); `numeroPlanejamento` da
I50 repete o nome da onda na TSP (a validar no homolog).

**Atualização (13/11/2026):** (1) XML solto na `PuxadaTSP` passou a ser **aceito** e sobe como TSP, em
uma onda sem título (`TSP-AAAAMMDD-NN`); ver 3.2 e 3.3. (2) XMLs em **subpastas** dentro do ZIP, em
qualquer profundidade, passam a ser lidos; ver 3.2. Só `.zip` é suportado como arquivo compactado
(decisão: sem `.rar`/`.7z`). A varredura de pastas fora do ZIP continua só no primeiro nível.

## 1. O problema

A BRF tem dois tipos de importação:

- **PUXADA:** a onda de separação é criada pelo transporte de cada nota (campo "Transporte:" do
  `infCpl`). É o que o integrador faz hoje.
- **PUXADA TSP:** as notas também trazem transporte, mas são carregadas **todas em um único
  veículo**. A onda é **única por TSP**, e o transporte de cada nota não define a onda.

Exemplo real (e-mail de 11/11/2026, assunto "Puxadas TSP´s Sorocaba e São José dos Campos"):
dois ZIPs, `XML'S SJC.zip` (2 notas, 2 transportes) e `XML'S SOROCABA.zip` (5 notas, 4 transportes),
todas da BRF, série 6. Média real: 150 XMLs por TSP, podendo chegar a 250. Hoje o processo é manual:
importam os XMLs e criam à mão um agrupamento de onda por TSP, com separação por onda única.

## 2. Decisões do usuário (fechadas)

1. O colaborador salva **somente os ZIPs** (não salva e-mail, não usa Outlook).
2. **Duas pastas:** `Puxada` e `PuxadaTSP`, dentro da pasta de entrada.
3. **Tudo que estiver em `PuxadaTSP` sobe como TSP. Tudo que estiver em `Puxada` sobe como puxada.**
   Não há detecção automática nem bloqueio de pasta errada.
4. **O relatório mostra o tipo de cada nota (`TSP` ou `PUXADA`)** e a onda a que pertence. Se o
   colaborador colocar na pasta errada, ele vê no relatório e corrige.
5. **Uma onda por ZIP** na TSP.
6. **Nome da onda da TSP:** prefixo `TSP-` + data + sequencial do dia + nome do ZIP. Se o ZIP não tiver nome
   aproveitável, sobe só com data e sequencial. O relatório mostra a onda, então dá para corrigir.
7. O sequencial pode ser **um por dia** (reinicia a cada dia).
8. **Guarda:** as notas (XML) ficam como hoje; o e-mail não é guardado.
9. Não sai nada disso para o manual da devolução (a tela da I40 continua fora do manual oficial).
10. **Placa (13/11/2026):** a **Puxada TSP vai SEMPRE sem placa** (`placaVeiculo` em branco na I50), seja ZIP ou
    XML solto em `PuxadaTSP`. Chave própria `I50:EnviarPlacaVeiculoTsp` (padrão `false`). A Puxada normal
    mantém `I50:EnviarPlacaVeiculo` (padrão `true`).

## 3. Regras de funcionamento

### 3.1 Pastas
Dentro da pasta de entrada (`C:\Friozem\kitbrf` em produção):

```
kitbrf\
  Puxada\       <- ZIP (ou XML solto) de puxada normal
  PuxadaTSP\    <- ZIP (ou XML solto) de puxada TSP
  Processados\  <- como hoje (pacote_<data>_<hora>\)
  Erro\         <- como hoje
  Estado\       <- como hoje
```

Compatibilidade assumida: XML solto na **raiz** da entrada continua sendo aceito e tratado como
`PUXADA` (é o processo de hoje), marcado no relatório como vindo da raiz. Se o usuário preferir
recusar a raiz, é só uma opção de configuração (`Pastas:AceitarRaiz=false`).

### 3.2 ZIP
- As pastas `PuxadaTSP` e `Puxada` aceitam ZIP e XML solto (só o primeiro nível da pasta é varrido).
  Só `.zip` é suportado como arquivo compactado (sem `.rar`/`.7z`).
- O ZIP é extraído para uma pasta temporária em `Estado`, os XMLs são processados e o ZIP original
  é guardado junto das notas no pacote.
- Só XML é aceito dentro do ZIP, **inclusive em subpastas de qualquer profundidade**. Outro tipo de
  arquivo cai como `ExtensaoInvalida`, como hoje. ZIP dentro de ZIP (em qualquer subpasta) não é aberto.
  O relatório mostra o caminho completo dentro do ZIP (ex.: `a/b/leia-me.txt`) das entradas ignoradas.
  Nomes iguais em subpastas diferentes não colidem (cada XML é extraído em uma pasta temporária própria).
- ZIP corrompido ou protegido por senha vai para `Erro\<pacote>\ZipInvalido` com `.motivo.txt`.
- Segurança (vale também para subpastas): nomes com caminho perigoso (`..`, caminho absoluto) são ignorados; limite de
  quantidade de arquivos e de tamanho total (configuráveis, com valores padrão); o mesmo XML repetido
  em dois ZIPs é processado uma vez e registrado como repetido.
- O ZIP só é aberto depois de pronto (detector de arquivo pronto que já existe).

### 3.3 Nome da onda da TSP
Formato: `TSP-AAAAMMDD-NN-NOME`, por exemplo `TSP-20261111-01-SOROCABA`.
(Decisão de 12/11/2026: o prefixo `TSP-` identifica a onda da Puxada TSP no WMS. A puxada normal
não muda: a onda continua sendo o número do transporte.)

- `TSP-`: prefixo fixo, só nas ondas da Puxada TSP.
- `AAAAMMDD`: data do processamento, no relógio do servidor.
- `NN`: sequencial do dia, com 2 dígitos (`01`, `02`, ...). Um novo número a cada ZIP TSP
  diferente no dia. Reinicia no dia seguinte. Guardado em `Estado`.
- `NOME`: nome do arquivo ZIP, limpo: sem extensão, sem a palavra `XML`/`XMLS` e sem apóstrofo,
  em maiúsculas, sem acento, só letras, números e hífen. Ex.: `XML'S SOROCABA.zip` -> `SOROCABA`;
  `XML'S SJC.zip` -> `SJC`.
- Se sobrar nome vazio: onda `TSP-AAAAMMDD-NN` (15 caracteres).
- **XML solto na `PuxadaTSP` (13/11/2026):** todos os XMLs soltos coletados no mesmo pacote formam
  **uma única onda** `TSP-AAAAMMDD-NN` (sem título), com o sequencial do dia compartilhado com as ondas
  de ZIP (os ZIPs continuam com uma onda por ZIP). **Não há reaproveitamento por hash:** se o usuário
  errou, apaga a onda no WMS e refaz o processo, e um novo sequencial é gerado. No relatório a Origem é
  `PuxadaTSP` e a coluna de ZIP fica vazia (é assim que se distingue de uma onda de ZIP).
- Limite do campo no WMS: `nroagrupseparacao` aceita **0 a 30** caracteres (manual da I50). A parte fixa
  `TSP-AAAAMMDD-NN-` ocupa 16 caracteres, então o NOME é cortado para **no máximo 14 caracteres**.
  `TSP-20261111-01-SOROCABA` tem 24 caracteres e cabe.
- **Reprocesso de ZIP:** o mesmo ZIP (mesmo conteúdo, por hash) reaproveita a mesma onda. Assim reenviar o
  mesmo ZIP não cria uma segunda onda.
- O DocCarga continua sendo **um por nota**; só o agrupador da onda muda.

### 3.4 Puxada normal
Sem mudança: onda = número do transporte da nota. Nota sem transporte vira pendência, como hoje. A placa do
veículo segue `I50:EnviarPlacaVeiculo` (padrão `true`); só a TSP é diferente (sem placa, ver decisão 10).

### 3.5 Relatório (HTML e PDF) e e-mail de fechamento
- Cada nota ganha a coluna **Tipo** (`PUXADA` ou `TSP`).
- Nas TSP, o agrupamento do relatório é por **onda** (`TSP-20261111-01-SOROCABA`), com a quantidade de
  notas e de transportes distintos, e a coluna **Transporte** mostra o transporte de cada nota.
- Nas puxadas normais, o agrupamento continua por transporte.
- Nota de TSP **sem** transporte no `infCpl` sobe normalmente (a TSP não depende do transporte); a
  coluna Transporte mostra "(sem)".
- O resumo do e-mail e o resumo semanal passam a contar notas por tipo.

## 4. Fora do escopo
- Ler e-mail (`.msg`/`.eml`) ou Outlook.
- Detecção automática de pasta errada ou bloqueio.
- Mudanças no WMS.
- Devolução (I40) e I60.

## 5. Estimativa (somente horas)

| Etapa | Horas humanas | Horas de IA |
|---|---:|---:|
| Pastas e tipo da nota, leitura segura do ZIP, nome e sequencial da onda, relatório e e-mail, testes, documentação e manuais | aprox. 3,5 | aprox. 6,0 |

A parte humana é o teste com ZIP real no homolog (duas rodadas) e a conferência da onda e do
relatório.

## 6. Brief para o workflow (execução em 12/11/2026)

Projeto KitWmsIntegrador (C#/.NET 8), repositório `c:\wms-integrador`, branch `main`, remote GitHub
**público**. Commits locais pequenos; **não fazer push** (o orquestrador faz). Windows/PowerShell,
responder e documentar em português. Fluxo: implementar + revisar em loop (revisor por último).

Regras do projeto:
- Esforço só em horas (`.kiro/steering/esforco-em-horas.md`), nunca valores monetários; não inventar
  horas humanas.
- **Nunca versionar `.xml`/`.XML` nem `.zip` com notas** (dados de cliente, repositório público; há
  `.gitignore` e hooks locais). Os testes devem **gerar** XMLs/ZIPs sintéticos em pasta temporária em
  tempo de execução, sem dados reais. Acrescentar `*.zip` ao `.gitignore`.
- Não enviar nada ao WMS e não acessar o servidor SRVAPPALC02. Não ligar nada em produção.
- Build: 0 avisos/0 erros (Debug e Release). Testes hoje: 487 aprovados, 9 ignorados.
- Não imprimir segredos. Sem dependência NuGet nova (usar `System.IO.Compression`).

Tarefas:
1. Configuração: `Pastas:Puxada` (padrão `Puxada`), `Pastas:PuxadaTsp` (padrão `PuxadaTSP`),
   `Pastas:AceitarRaiz` (padrão `true`), `Zip:MaxArquivos` e `Zip:MaxTamanhoMb` com valores padrão
   seguros. Atualizar `appsettings.json` do Worker e `deploy\config-exemplo\appsettings.Production.json`.
2. Tipo da nota (`PUXADA` ou `TSP`) propagado pelo pipeline (classificação da entrada, pacote,
   diário do pacote, telemetria se couber) e pastas monitoradas pelo watcher (as duas subpastas e a raiz
   se `AceitarRaiz`). Não monitorar `Processados`, `Erro` e `Estado`.
3. Leitor seguro de ZIP (extração em `Estado\tmp`, proteção contra caminho perigoso e ZIP bomba,
   só XML, sem ZIP aninhado, limites), nova categoria de erro `ZipInvalido` com `.motivo.txt`,
   guarda do ZIP original no pacote e XMLs em `Processados\pacote_*\` (a busca da devolução depende
   desse layout).
4. Nome da onda TSP e sequencial do dia persistido em `Estado\tsp\` com hash do ZIP para reprocesso
   (regra 3.3), incluindo o corte em 30 caracteres.
5. Agrupamento: TSP = uma onda por ZIP (todas as notas do ZIP com o mesmo `nroagrupseparacao`);
   PUXADA = como hoje. DocCarga continua 1 por nota. Nota de TSP sem transporte não vira pendência.
6. Relatório HTML/PDF e e-mail de fechamento: coluna Tipo, agrupamento da TSP por onda com contagem
   de notas e transportes, coluna Transporte; resumo semanal conta por tipo.
7. Testes: nome da onda (casos reais `XML'S SOROCABA.zip` e `XML'S SJC.zip`, nome vazio, acentos,
   corte em 30), sequencial do dia e virada de dia, reprocesso do mesmo ZIP, ZIP inválido, ZIP com
   arquivo não-XML, caminho perigoso, ZIP aninhado, XML repetido em dois ZIPs, tipo por pasta,
   raiz aceita/recusada, agrupamento TSP x PUXADA, relatório com Tipo.
8. Documentação: `docs\ARQUITETURA.md`, `docs\OPERACAO.md`, `docs\INSTALACAO-SERVIDOR.md` (criar as
   pastas e compartilhamento), manuais do usuário e técnico (como depositar em `Puxada` e `PuxadaTSP`,
   o que o relatório mostra), `docs\ENCERRAMENTO.md`, diário do dia. **Sem** devolução nos manuais.
   Regenerar os manuais (`docs\manuais\gerar-manuais.ps1` e PDF pelo Edge, ver `docs\manuais\README.md`).
   Atualizar a apresentação (`docs\apresentacao\apresentacao.html`, edições cirúrgicas, UTF-8 com BOM,
   conferir por screenshot e regenerar o PDF) com a linha de 11/11 (2,0 h humanas e aprox. 3,5 h de IA)
   e a Puxada TSP em andamento.
9. Verificação: build, testes, `git ls-files` sem `.xml`/`.zip`, `Habilitada=false` nos appsettings
   versionados onde já era `false`, sem valores monetários. Relatório em
   `.agents\tasks\puxada-tsp.md`.
