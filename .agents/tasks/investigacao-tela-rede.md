# Investigação: tela de devolução (I40) acessível pela rede por link

Somente leitura. Nada foi alterado nem consultado no servidor de produção.

## 1. Resumo executivo

**O que já funciona:** o código já está pronto para uso em rede. O Kestrel sobe em `http://0.0.0.0:<Devolucao:Porta>` (todas as interfaces), porta padrão 8085, HTTP simples. A página é embutida no executável e usa URLs relativas, então abre por nome (`http://SRVAPPALC02:8085/`) ou por IP (`http://200.200.200.213:8085/`) sem mudar código.

**O que falta (nenhum item é código):**
1. Ligar a tela: `Devolucao:Habilitada=true` (hoje `false`) e reiniciar o serviço.
2. Criar a regra de firewall de entrada na 8085, restrita à sub-rede. Nenhum script de `deploy\*.ps1` faz isso; só existe o exemplo manual em `docs\INSTALACAO-SERVIDOR.md` seção 3.12.
3. Decidir o controle de acesso. Hoje não há login, nem identificação do usuário. Só o IP de origem vai para a auditoria.

**Risco principal:** qualquer máquina que alcance a porta pode enviar I40 ao WMS de produção, sem saber quem foi. A tela está desligada e nunca foi testada em produção, nem na tela real em homolog (roteiro seção 9 pendente).

**Recomendação:** não ligar direto. Sequência: (a) rodar o roteiro seção 9 no homolog; (b) publicar com a tela desligada; (c) ligar com firewall restrito à sub-rede (opção A). Em seguida, acrescentar identificação do usuário (opção B, Windows Integrated/Negotiate restrito a grupo do AD) antes de ampliar o uso. A opção C (chave de acesso compartilhada) é o meio-termo mais rápido.

## 2. Evidências

### 2.1 Bind, porta, protocolo e arquivos estáticos

- `src\KitWmsIntegrador.Worker\TelaDevolucao\ServidorTelaDevolucao.cs`:
  - L32-37: só liga se `opcoes.Habilitada`; senão loga "desligada" e retorna.
  - L38-43: valida `Porta` entre 1 e 65535.
  - L46: URL **fixa** `$"http://0.0.0.0:{opcoes.Porta}"`. O endereço NÃO é configurável; só a porta.
  - L50: o log diz "sem login; restrinja pelo firewall".
- `src\KitWmsIntegrador.Worker\TelaDevolucao\TelaDevolucaoApp.cs` L39: `builder.WebHost.UseUrls(urls)`. Kestrel via `WebApplication.CreateSlimBuilder`. Não há HTTPS, nem `UseHttps`, nem certificado.
- `src\KitWmsIntegrador.Core\Configuracao\OpcoesDevolucao.cs`: `Habilitada` L37 (default false), `Porta` L71 (default `PortaPadrao`=8085, L19), `MaxChavesPorEnvio` L46 (30), `PastaAuditoria` L51.
- Config de produção: `deploy\config-exemplo\appsettings.Production.json` L74-81 (`Habilitada:false`, `Porta:8085`). Override por variável: `Devolucao__Habilitada`, `Devolucao__Porta`.
- Arquivos estáticos: `src\KitWmsIntegrador.Worker\KitWmsIntegrador.Worker.csproj` L22-23 embute `PaginaDevolucao.html` e `logo-friozem.b64.txt` como `EmbeddedResource`. `TelaDevolucaoApp.cs` (`CarregarPagina`, `LerRecurso`) serve uma página única em `GET /` com o logo em base64. Não há JS/CSS externos.
- URLs relativas: `PaginaDevolucao.html` L311 `fetch('/api/devolucao/buscar?...')`, L385 `fetch('/api/devolucao/enviar', POST)`, L416 `fetch('/api/config')`. Mesma origem, sem host fixo, então funcionam por hostname ou IP.
- CSP (`TelaDevolucaoApp.cs`, middleware ~L55-63): `default-src 'self'; img-src data:; style-src/script-src 'unsafe-inline'; frame-ancestors 'none'`, mais `X-Frame-Options: DENY`, `nosniff`, `no-store`. Compatível com acesso remoto. O HTML usa `textContent` (L174-200), o que reduz risco de XSS.
- Windows Server 2012 R2: Kestrel usa sockets, sem HTTP.sys, então **não exige `netsh http add urlacl`** (`docs\INSTALACAO-SERVIDOR.md` L349; `docs\ARQUITETURA-PLANO-B-I40.md` L130-133 e L372-373). O .NET 8 self-contained já roda no servidor sob NSSM.

### 2.2 Endpoints (todos sem autenticação)

`TelaDevolucaoApp.cs`:
- `GET /` (L65), `GET /api/saude` (L67), `GET /api/config` (L69)
- `GET /api/devolucao/buscar?numero=&serie=` (L74): lê só nomes de arquivo na retenção.
- `POST /api/devolucao/enviar` (L104): corpo JSON `{chaves:[...]}`. Monta e **envia a I40 ao WMS** com a conta de serviço do WMS (`WMS__Usuario`/`WMS__Senha`). `Origem = RemoteIpAddress` (L126).

### 2.3 Autenticação e proteções

- **Autenticação/autorização: nenhuma.** Não há `UseAuthentication`, `RequireAuthorization` nem Negotiate. Confirmado em `docs\OPERACAO.md` L430, `docs\INSTALACAO-SERVIDOR.md` L347 ("o controle de acesso é o firewall") e `ARQUITETURA-PLANO-B-I40.md` L175 e L384-386 (risco R4, aceito).
- Quem pode enviar I40 se a porta abrir: qualquer host que alcance a 8085 (qualquer usuário de qualquer máquina da sub-rede liberada, ou da rede inteira se a regra não restringir). Usa a credencial do WMS do serviço, então no WMS aparece sempre como a mesma conta.
- CSRF: sem token, cookie ou autenticação. O POST é `application/json` via `ReadFromJsonAsync` (que exige content-type JSON). Um formulário HTML cruzado não consegue enviar esse tipo, mas um fetch de outra origem dispara pré-voo CORS. Como **não há CORS configurado** (nenhum `AddCors`), o navegador bloqueia a resposta, mas a requisição "simples" não é a do caso. Risco prático de CSRF é baixo; a defesa real hoje é a rede.
- Filtro de host (`AllowedHosts`): não configurado; aceita qualquer `Host`. Útil aqui, pois nome e IP funcionam.
- Rate limit: nenhum. Limites existentes: corpo máx. 64 KB (L28, usado em `MaxRequestBodySize`) e `MaxChavesPorEnvio` 30 (`ServicoDevolucao.cs` L73-83).
- Validação de entrada: boa. Normaliza e deduplica chaves, 44 dígitos e dígito verificador mod 11 (`ValidadorChavesDevolucao`, `LocalizadorXmlDevolucao.ValidarChave`). Só localiza XML por `NFE<chave>.XML` em `Processados\pacote_*` (retenção 60 dias), sem path livre. Número/série validados em `ValidadorChavesDevolucao.ValidarNumeroSerie`. Só enviam notas já integradas.
- Auditoria: `src\KitWmsIntegrador.Core\Devolucao\AuditoriaDevolucao.cs`, arquivo diário `devolucao_yyyyMMdd.log` com `data;ip;chave;nota;status;transacao;mensagem`, em `<Estado>\devolucao` (chamada em `ServicoDevolucao.cs` L168). **Registra IP, não usuário.** Atrás de NAT ou proxy o IP perde valor.
- Duplicidade: não há histórico local; quem recusa duplicidade é o WMS (`ARQUITETURA-PLANO-B-I40.md` L174). Ponto em aberto P7.

### 2.4 Riscos de expor na LAN

1. Envio sem identificação e sem aprovação (qualquer pessoa na rede liberada).
2. Sem TLS: tráfego em claro (chaves de NF-e; sem senha, pois não há login). Aceitável em LAN interna, mas impede cookie seguro ou credenciais futuras em texto simples.
3. Escopo da regra de firewall: se for `Any` ou perfil Public/Private, vira exposição ampla. O exemplo da doc usa `-Profile Domain`.
4. Auditoria só com IP.
5. Falha de bind (porta ocupada) não derruba o Worker: erro logado e a tela fica desligada (`ServidorTelaDevolucao.cs`, catch final).
6. Notas devolvidas por engano geram I40 real no WMS de produção.

### 2.5 Outras telas que listam notas

Não há outra tela web. Busca em `src\**\*.cs` encontrou `MapGet`/`MapPost` somente em `TelaDevolucaoApp.cs`. O resto sai como arquivos: relatórios HTML/PDF da I51/I60 e de erros (seção `Relatorio`) e envio por e-mail. A pasta de entrada é exposta por SMB via `deploy\compartilhar-pasta.ps1` (share `kitbrf`). Opção de ver relatórios na rede: compartilhar a pasta do relatório por SMB (não implementado nem verificado aqui).

## 3. O que é preciso para abrir por link

| Item | Ação | Já coberto pelos scripts? |
|---|---|---|
| Config | `config\appsettings.Production.json`, bloco `Devolucao`: `"Habilitada": true` (porta 8085 já é o default). Alternativa: `Devolucao__Habilitada=true`. Reiniciar o serviço (`deploy\servico.ps1 -Acao Restart`) | Não. `instalar.ps1` e `publicar-release.ps1` não tocam em `Devolucao` (grep em `deploy\*.ps1` só acha o exemplo de config). `publicar-release.ps1` copia a config e reinicia o serviço. |
| Firewall (PowerShell admin, no servidor) | `New-NetFirewallRule -DisplayName 'Friozem Devolucao I40 (8085)' -Direction Inbound -Protocol TCP -LocalPort 8085 -RemoteAddress '<SUB-REDE/CIDR>' -Action Allow -Profile Domain`. Remoção: `Remove-NetFirewallRule -DisplayName 'Friozem Devolucao I40 (8085)'`. Equivalente `netsh`: `netsh advfirewall firewall add rule name="Friozem Devolucao I40 (8085)" dir=in action=allow protocol=TCP localport=8085 remoteip=<SUB-REDE/CIDR> profile=domain`. A sub-rede real deve ser informada pelo usuário/TI (não consta nos docs; o IP do servidor é 200.200.200.213). | Não. Só documentado em `docs\INSTALACAO-SERVIDOR.md` L344-375, explicitamente "NÃO executado pela instalação". |
| URL ACL | **Não é necessário** (Kestrel, sockets). `netsh http add urlacl` só serviria para HttpListener/http.sys. LocalSystem vincula a porta sem ajuste. | n/a |
| DNS/hostname | `SRVAPPALC02` resolve se estiver no DNS/domínio. Senão usar o IP, ou pedir um CNAME (ex.: `devolucao.<dominio>`) ao TI. Sem TLS, não há certificado a casar. | Não |
| Teste | De outra máquina: `http://SRVAPPALC02:8085/api/saude` deve devolver `{"status":"ok"}` (`INSTALACAO-SERVIDOR.md` L371-372). | Não |

## 4. Opções de desenho (esforço só em horas)

Estimativas minhas, não medidas; horas humanas dependem do usuário/TI e devem ser confirmadas por quem as executa.

**A. Mínima: firewall por sub-rede (sem mudar código).** Ligar `Habilitada`, criar a regra restrita ao CIDR, reiniciar e testar de outra máquina. Controle = rede. Sem identificação do usuário.
- IA: 0,3 h (checklist e script de firewall opcional). Humano: 1,0 h (TI/usuário: sub-rede, regra, teste).

**B. Windows Integrated Authentication (Negotiate/Kerberos) restrita a grupo do AD.** Pacote `Microsoft.AspNetCore.Authentication.Negotiate`, `RequireAuthorization` com policy de grupo, chave `Devolucao:GrupoPermitido`, e gravar `DOMINIO\usuario` na auditoria (novo campo no log). Kestrel suporta Negotiate no Windows. O serviço roda como LocalSystem, que autentica como a conta do computador (Kerberos com SPN `HTTP/SRVAPPALC02` pode exigir registro do SPN; senão cai em NTLM). Precisa de testes no AD e no navegador de cada estação (intranet zone). Aumenta o pacote self-contained pouco, sem mudar o fluxo.
- IA: 2,5 h (código, testes, docs). Humano: 2,5 h (grupo no AD, SPN, testes em 2 ou 3 estações). Risco: depende do domínio; reservar tempo de ajuste.

**C. Chave de acesso compartilhada + nome informado.** Cabeçalho ou campo de login simples (`Devolucao:ChaveAcesso`, em env/segredo), mais campo "Seu nome" obrigatório gravado na auditoria. Barato, mas nome é autodeclarado e a chave é compartilhada.
- IA: 1,5 h. Humano: 1,0 h (definir e distribuir a chave, testes).

**D. Complementos (qualquer opção).** Registrar `User-Agent`/host na auditoria; rate limit simples por IP; HTTPS com certificado interno (se B ou C passarem a trafegar credenciais, B com Kerberos já protege o ticket). HTTPS: IA 1,5 h, humano 1,5 h (certificado, 2012 R2).

Recomendação: A para começar, após o teste de homolog; B como alvo (identifica quem fez). C se B não for viável no AD.

## 5. Conflito com `Habilitada=false` e teste de homolog nunca feito

Não há conflito técnico: o `false` é exatamente o que mantém a porta fechada hoje. Ligar é uma mudança de config e reinício, não de código. Mas há dependências de processo:
- `docs\ROTEIRO-TESTE-I40.md` seção 9 (L132-193) **ainda não foi executada** com a tela real (registrado em `docs\diario\2026-11-10.md` L96 "envio real pela tela ainda não foi testado" e `docs\ENCERRAMENTO.md` L87). Testes unitários e de integração com fakes passam (451), mas o envio real ao WMS pela tela não foi validado. O modo manual `--enviar-i40` foi validado (IMP, "Registrado com sucesso").
- O `INSTALACAO-SERVIDOR.md` seção 3.12 manda publicar com a tela desligada, confirmar o serviço, criar o firewall e **só então** ligar, e testar com uma nota real combinada com o responsável do WMS.
- Em produção, o envio usa a conta de serviço e gera I40 real. Sem homolog validado, o risco é alto. O teste de homolog em máquina local (`localhost:8085`) não exige firewall nem rede.
- O Worker está agora no meio dos testes I51/I60. Ligar a tela exige reinício do serviço, o que interrompe o ciclo por instantes. Fazer em janela combinada.

## 6. Conclusão e próximos passos sugeridos

1. Executar o roteiro seção 9 no homolog (localhost) e registrar o resultado.
2. Decidir A, B ou C (item 4); a informação necessária do usuário é a sub-rede (CIDR) e se há grupo do AD.
3. Se A: publicar com tela desligada, criar a regra de firewall, ligar `Habilitada`, reiniciar em janela combinada, testar `/api/saude` e uma nota real com o responsável do WMS.
4. Se B ou C: implementar antes de ligar (itens fora do escopo desta investigação).

Limites da investigação: não li a seção 3.12 além do trecho citado nem executei testes. Não verifiquei o estado atual do servidor, DNS, sub-rede, AD ou se `SRVAPPALC02` resolve na rede. A pasta exata dos relatórios (item 2.5) não foi confirmada.
