using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Pacote;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Renderiza o relatório como HTML autocontido: CSS inline, sem recursos externos, logo em
/// base64 (quando configurado) e filtro opcional em JavaScript puro inline. Sem JS, todas as
/// linhas permanecem visíveis.
/// </summary>
internal static class RenderizadorHtml
{
    private const string Css = @"
*{box-sizing:border-box}
body{font-family:Arial,Helvetica,sans-serif;color:#333333;margin:0;padding:24px;background:#ffffff;font-size:13px}
.faixa{display:flex;align-items:center;gap:22px;background:#17479E;color:#ffffff;padding:14px 22px;border-radius:6px 6px 0 0}
.faixa img{height:52px;width:auto;display:block;flex:none}
.marca{font-size:30px;font-weight:bold;color:#ffffff;letter-spacing:.5px}
.faixa h1{font-size:26px;line-height:1.15;margin:0;color:#ffffff}
.faixa .sub{margin:3px 0 0;font-size:12px;letter-spacing:.4px;color:#d6efff}
.trio{display:flex;height:5px}.trio i{display:block;flex:1}
.trio i:nth-child(1){background:#17479E}.trio i:nth-child(2){background:#54B6E7}.trio i:nth-child(3){background:#009A83}
.info{display:flex;flex-wrap:wrap;gap:6px 0;margin:0 0 18px;padding:9px 16px;background:#f5f9fd;border:1px solid #C1C1C1;border-top:0;border-radius:0 0 6px 6px;font-size:13px}
.info span{padding-right:16px;margin-right:16px;border-right:1px solid #C1C1C1}
.info span:last-child{border-right:0;margin-right:0;padding-right:0}
h1{font-size:22px;margin:0;color:#17479E}
h3{font-size:13px;margin:14px 0 0;color:#17479E}
table.tmp{width:auto;min-width:420px;max-width:100%}
.tmp thead th{position:static}
.tmp td.num{text-align:right;white-space:nowrap}
h2{font-size:16px;margin:22px 0 0;color:#17479E}
.barra{display:flex;width:64px;height:4px;margin:6px 0 12px}
.barra i{display:block;flex:1}
.barra i:nth-child(1){background:#17479E}.barra i:nth-child(2){background:#54B6E7}.barra i:nth-child(3){background:#009A83}
.cards{display:flex;flex-wrap:wrap;gap:10px;margin:8px 0}
.card{border:1px solid #C1C1C1;border-top:4px solid #54B6E7;border-radius:4px;padding:8px 14px;min-width:130px}
.card b{display:block;font-size:24px;color:#17479E}
.card.ok{border-top-color:#009A83}.card.ok b{color:#009A83}
.card.rev{border-top-color:#009A83}
.card.pen{border-top-color:#F5A623}.card.pen b{color:#B8741A}
.card.nao{border-top-color:#D32F2F}.card.nao b{color:#D32F2F}
.card.ign{border-top-color:#6B7280}.card.ign b{color:#6B7280}
.porte{margin:6px 0;color:#333333}
.chip{display:inline-block;border:1px solid #C1C1C1;border-radius:12px;padding:2px 10px;margin:2px 6px 2px 0;background:#f5f9fd}
table{border-collapse:collapse;width:100%;margin-top:8px}
th,td{border:1px solid #C1C1C1;padding:5px 8px;text-align:left;vertical-align:top}
thead th{position:sticky;top:0;background:#17479E;color:#ffffff;z-index:2}
tr.grupo td{background:#e8f4fb;color:#17479E;font-weight:bold;border-left:4px solid #54B6E7}
tr[hidden]{display:none}
.st{display:inline-block;border-radius:3px;padding:1px 8px;color:#ffffff;font-weight:bold;white-space:nowrap}
.st-integrada{background:#009A83}.st-revisar{background:#009A83}
.st-pendente{background:#F5A623;color:#333333}.st-nao-integrada{background:#D32F2F}
.st-ignorado{background:#6B7280}
.tipo{display:inline-block;border-radius:3px;padding:1px 6px;font-weight:bold;font-size:11px;white-space:nowrap;background:#e8f4fb;color:#17479E}
.tipo-tsp{background:#fdf3e0;color:#8a5a12}
.raiz{display:inline-block;border:1px solid #C1C1C1;border-radius:10px;padding:0 7px;font-size:11px;color:#6B7280;background:#f4f4f4}
.revmarca{display:block;margin-top:3px;font-size:11px;color:#17479E}
.id{white-space:nowrap}
.msg{min-width:280px}
details{margin-top:4px}summary{cursor:pointer;color:#17479E}
details div{margin:4px 0;white-space:pre-wrap;word-break:break-word}
details ol{margin:4px 0 0 18px;padding:0;font-size:12px}
.controles{display:flex;gap:10px;align-items:center;margin:10px 0}
.controles input,.controles select{padding:5px 8px;border:1px solid #C1C1C1;border-radius:3px;font-family:Arial,Helvetica,sans-serif}
.nota{color:#333333;font-size:12px}
@media print{body{padding:0}.faixa,.info,.trio i,thead th,.card{-webkit-print-color-adjust:exact;print-color-adjust:exact}thead th{position:static}.controles{display:none}details>*{display:block}}
";

    private const string Script = @"
(function(){
var c=document.getElementById('controles');if(!c){return;}
c.hidden=false;
var q=document.getElementById('busca'),s=document.getElementById('filtro-status'),n=document.getElementById('contagem');
var linhas=[].slice.call(document.querySelectorAll('tr[data-status]'));
var grupos=[].slice.call(document.querySelectorAll('tr.grupo'));
function aplicar(){
var t=(q.value||'').toLowerCase(),st=s.value,vis=0,cont={};
linhas.forEach(function(l){
var ok=(!st||l.getAttribute('data-status')===st)&&(!t||l.getAttribute('data-texto').indexOf(t)>=0);
l.hidden=!ok;
if(ok){vis++;cont[l.getAttribute('data-grupo')]=true;}
});
grupos.forEach(function(g){g.hidden=!cont[g.getAttribute('data-grupo')];});
n.textContent='Mostrando '+vis+' de '+linhas.length;
}
q.addEventListener('input',aplicar);
s.addEventListener('change',aplicar);
aplicar();
})();
";

    public static string Renderizar(
        ModeloRelatorio modelo, DateTimeOffset geradoEm, byte[]? logoPng, PeriodoColeta? periodo = null,
        DadosPacote? pacote = null)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.Append("<title>").Append(Esc(IdentidadeVisual.Titulo)).Append("</title>");
        sb.Append("<style>").Append(Css).Append("</style></head><body>\n");

        // Cabeçalho: faixa azul da marca com a logo negativa (base64) ou o texto "Friozem",
        // título em destaque e uma linha de informações organizada (pacote, geração, coleta).
        sb.Append("<header><div class=\"faixa\">");
        if (logoPng is not null)
        {
            sb.Append("<img alt=\"Friozem\" src=\"data:image/png;base64,")
              .Append(Convert.ToBase64String(logoPng)).Append("\" />");
        }
        else
        {
            sb.Append("<span class=\"marca\">Friozem</span>");
        }

        sb.Append("<div><h1>").Append(Esc(IdentidadeVisual.Titulo)).Append("</h1>")
          .Append("<p class=\"sub\">KitWms Integrador</p></div></div>");
        sb.Append("<div class=\"trio\"><i></i><i></i><i></i></div>");
        sb.Append("<div class=\"info\">");
        foreach (var (rotulo, valor) in IdentidadeVisual.ItensInformacao(modelo.Resumo, geradoEm, periodo, pacote))
        {
            sb.Append("<span><b>").Append(Esc(rotulo)).Append("</b> ").Append(Esc(valor)).Append("</span>");
        }

        sb.Append("</div></header>\n");

        var bloco = BlocoTempo.Montar(modelo.Resumo, pacote?.Medicao);
        AppendResumo(sb, modelo.Resumo, bloco);
        AppendTempo(sb, bloco);
        AppendProdutosRevisao(sb, modelo);
        AppendTabela(sb, modelo);

        sb.Append("<script>").Append(Script).Append("</script>\n");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Barra() => "<div class=\"barra\"><i></i><i></i><i></i></div>";

    private static void AppendResumo(StringBuilder sb, ResumoRelatorio r, BlocoTempo? bloco)
    {
        sb.Append("<h2>Resumo</h2>").Append(Barra());
        sb.Append("<div class=\"cards\">");
        Card(sb, "", "Notas processadas", r.Total);
        Card(sb, "ok", "Integradas", r.Integradas);
        Card(sb, "rev", "Integradas — revisar produto", r.IntegradasRevisarProduto);
        Card(sb, "pen", "Pendentes", r.Pendentes);
        Card(sb, "nao", "Não integradas", r.NaoIntegradas);
        Card(sb, "ign", "Arquivos ignorados", r.ArquivosIgnorados);
        Card(sb, "", "Transportes", r.Transportes);
        sb.Append("</div>");
        sb.Append("<p class=\"porte\"><b>Por tipo:</b> ")
          .Append("<span class=\"chip\">PUXADA: ").Append(Esc(IdentidadeVisual.Plural(r.NotasPuxada, "nota", "notas"))).Append("</span>")
          .Append("<span class=\"chip\">TSP: ").Append(Esc(IdentidadeVisual.Plural(r.NotasTsp, "nota", "notas"))).Append("</span>")
          .Append("<span class=\"chip\">Notas da raiz: ").Append(r.NotasRaiz.ToString(CultureInfo.InvariantCulture)).Append("</span>")
          .Append("</p>\n");
        // Com o bloco de tempo, a tabela por onda já traz notas e transportes: não repete os chips.
        if (r.NotasPorOnda is { Count: > 0 } && bloco is not { Ondas.Count: > 0 })
        {
            sb.Append("<p class=\"porte\"><b>Ondas da Puxada TSP:</b> ");
            foreach (var (onda, notas, transportes) in r.NotasPorOnda)
            {
                sb.Append("<span class=\"chip\">").Append(Esc(onda)).Append(": ")
                  .Append(Esc(IdentidadeVisual.Plural(notas, "nota", "notas"))).Append(", ")
                  .Append(Esc(IdentidadeVisual.Plural(transportes, "transporte", "transportes"))).Append("</span>");
            }

            sb.Append("</p>\n");
        }

        // A linha "Notas por transporte: ..." saiu do resumo (pedido de 12/11): a mesma informação
        // segue nos cabeçalhos de cada grupo da tabela ("Transporte X — N notas · ...").
    }

    // Bloco "Tempo de integração" (nível B): resumo do pacote, etapas e, só na TSP, tempo por onda.
    private static void AppendTempo(StringBuilder sb, BlocoTempo? bloco)
    {
        if (bloco is null)
        {
            return;
        }

        sb.Append("<h2>Tempo de integração</h2>").Append(Barra());
        sb.Append("<h3>Resumo do pacote</h3>");
        sb.Append("<table class=\"tmp\"><tbody>");
        foreach (var l in bloco.Resumo)
        {
            sb.Append("<tr><th scope=\"row\">").Append(Esc(l.Rotulo)).Append("</th><td class=\"num\">")
              .Append(Esc(l.Valor)).Append("</td></tr>");
        }

        sb.Append("</tbody></table>\n");

        if (bloco.Etapas.Count > 0)
        {
            sb.Append("<h3>Etapas</h3>");
            sb.Append("<table class=\"tmp\"><thead><tr><th>Etapa</th><th>Duração</th><th>Por nota</th></tr></thead><tbody>");
            foreach (var e in bloco.Etapas)
            {
                sb.Append("<tr><td>").Append(Esc(e.Etapa)).Append("</td><td class=\"num\">").Append(Esc(e.Duracao))
                  .Append("</td><td class=\"num\">").Append(Esc(e.PorNota)).Append("</td></tr>");
            }

            sb.Append("</tbody></table>\n");
        }

        if (bloco.Ondas.Count > 0)
        {
            sb.Append("<h3>Por onda (Puxada TSP)</h3>");
            sb.Append("<table class=\"tmp\"><thead><tr><th>Onda</th><th>Notas</th><th>Transportes</th><th>Tempo da onda</th></tr></thead><tbody>");
            foreach (var o in bloco.Ondas)
            {
                sb.Append("<tr><td>").Append(Esc(o.Onda)).Append("</td><td class=\"num\">").Append(Esc(o.Notas))
                  .Append("</td><td class=\"num\">").Append(Esc(o.Transportes))
                  .Append("</td><td class=\"num\">").Append(Esc(o.Tempo)).Append("</td></tr>");
            }

            sb.Append("</tbody></table>\n");
        }

        sb.Append("<p class=\"nota\">").Append(Esc(BlocoTempo.Nota)).Append("</p>\n");
    }

    private static void Card(StringBuilder sb, string classe, string rotulo, int valor)
    {
        sb.Append("<div class=\"card ").Append(classe).Append("\"><b>")
          .Append(valor.ToString(CultureInfo.InvariantCulture)).Append("</b>")
          .Append(Esc(rotulo)).Append("</div>");
    }

    private static void AppendProdutosRevisao(StringBuilder sb, ModeloRelatorio modelo)
    {
        if (modelo.ProdutosRevisao.Count == 0)
        {
            return;
        }

        sb.Append("<h2>Produtos criados por contenção (revisar cadastro)</h2>").Append(Barra());
        sb.Append("<p class=\"nota\">Códigos enviados à I30 como produto provisório. ")
          .Append("O WMS não informa quais foram de fato criados; a lista traz todos os itens enviados.</p>");
        sb.Append("<table><thead><tr><th>Nota</th><th>Transporte</th><th>Produtos provisórios</th></tr></thead><tbody>");
        foreach (var p in modelo.ProdutosRevisao)
        {
            sb.Append("<tr><td>").Append(Esc(p.Nota)).Append("</td><td>").Append(Esc(p.Transporte))
              .Append("</td><td>").Append(Esc(string.Join(", ", p.Produtos))).Append("</td></tr>");
        }

        sb.Append("</tbody></table>\n");
    }

    private static void AppendTabela(StringBuilder sb, ModeloRelatorio modelo)
    {
        sb.Append("<h2>Notas por transporte</h2>").Append(Barra());

        // Controles ocultos por padrão; o script os revela (sem JS, só a tabela completa).
        sb.Append("<div class=\"controles\" id=\"controles\" hidden>")
          .Append("<input type=\"search\" id=\"busca\" placeholder=\"Buscar nota, transporte ou mensagem\" aria-label=\"Buscar\" />")
          .Append("<select id=\"filtro-status\" aria-label=\"Filtrar por status\">")
          .Append("<option value=\"\">Todos os status</option>")
          .Append("<option value=\"integrada\">Integrada</option>")
          .Append("<option value=\"revisar\">Integrada — revisar produto</option>")
          .Append("<option value=\"pendente\">Pendente</option>")
          .Append("<option value=\"nao-integrada\">Não integrada</option>")
          .Append("<option value=\"ignorado\">Arquivo ignorado (não é XML)</option>")
          .Append("</select><span id=\"contagem\"></span></div>\n");

        sb.Append("<table><thead><tr>");
        foreach (var h in new[]
                 {
                     "Nota", "Transporte", "Tipo", "Origem", "Horário", "Status", "ID de processamento",
                     "Mensagem", "Integrada", "Tentativas",
                 })
        {
            sb.Append("<th>").Append(Esc(h)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>\n");

        foreach (var g in modelo.Grupos)
        {
            sb.Append("<tr class=\"grupo\" data-grupo=\"").Append(Attr(g.Transporte)).Append("\"><td colspan=\"10\">");
            if (g.EhTsp)
            {
                sb.Append("Onda ").Append(Esc(g.Transporte)).Append(" (TSP) — ")
                  .Append(Esc(IdentidadeVisual.Plural(g.Notas, "nota", "notas"))).Append(" · ")
                  .Append(Esc(IdentidadeVisual.Plural(g.Transportes, "transporte", "transportes"))).Append(" · ");
            }
            else
            {
                sb.Append("Transporte ").Append(Esc(g.Transporte)).Append(" — ")
                  .Append(Esc(IdentidadeVisual.Plural(g.Notas, "nota", "notas"))).Append(" · ");
            }

            sb.Append(Esc(IdentidadeVisual.Plural(g.Integradas, "integrada", "integradas"))).Append(" · ")
              .Append(g.ComProblema.ToString(CultureInfo.InvariantCulture)).Append(" com problema")
              .Append("</td></tr>\n");

            foreach (var l in g.Linhas)
            {
                AppendLinha(sb, l);
            }
        }

        sb.Append("</tbody></table>\n");
    }

    private static void AppendLinha(StringBuilder sb, LinhaRelatorioErro l)
    {
        var chave = ChaveStatus(l);
        var texto = $"{l.NumeroNota} {l.NumeroTransporte} {l.Tipo} {l.Origem} {l.Onda} {l.Zip} {l.Mensagem} {l.IdTransacao} {l.IdDocumento}".ToLowerInvariant();
        var grupo = l.EhTsp ? (string.IsNullOrWhiteSpace(l.Onda) ? ModeloRelatorio.SemOnda : l.Onda) : l.NumeroTransporte;

        sb.Append("<tr data-status=\"").Append(chave)
          .Append("\" data-grupo=\"").Append(Attr(grupo))
          .Append("\" data-transporte=\"").Append(Attr(l.NumeroTransporte))
          .Append("\" data-nota=\"").Append(Attr(l.NumeroNota))
          .Append("\" data-texto=\"").Append(Attr(texto)).Append("\">");
        sb.Append("<td>").Append(Esc(l.NumeroNota)).Append("</td>");
        sb.Append("<td>").Append(Esc(l.NumeroTransporte)).Append("</td>");
        sb.Append("<td><span class=\"tipo").Append(l.EhTsp ? " tipo-tsp" : string.Empty).Append("\">")
          .Append(Esc(l.EhTsp ? "TSP" : "PUXADA")).Append("</span></td>");
        sb.Append("<td>");
        if (l.EhRaiz)
        {
            sb.Append("<span class=\"raiz\">raiz</span>");
        }
        else
        {
            sb.Append(string.IsNullOrWhiteSpace(l.Origem) ? "-" : Esc(l.Origem));
        }

        sb.Append("</td>");
        sb.Append("<td>").Append(Esc(IdentidadeVisual.Horario(l.HorarioErro))).Append("</td>");

        sb.Append("<td><span class=\"st st-").Append(chave).Append("\">").Append(Esc(l.StatusDescricao)).Append("</span>");
        if (l.RevisaoProdutoPendente)
        {
            sb.Append("<span class=\"revmarca\">revisar produto: ")
              .Append(Esc(string.Join(", ", l.ProdutosProvisorios!))).Append("</span>");
        }

        sb.Append("</td>");

        sb.Append("<td class=\"id\">");
        if (string.IsNullOrWhiteSpace(l.IdTransacao) && string.IsNullOrWhiteSpace(l.IdDocumento))
        {
            sb.Append('-');
        }
        else
        {
            sb.Append("Transação: ").Append(Esc(string.IsNullOrWhiteSpace(l.IdTransacao) ? "-" : l.IdTransacao))
              .Append("<br />Documento: ").Append(Esc(string.IsNullOrWhiteSpace(l.IdDocumento) ? "-" : l.IdDocumento));
        }

        sb.Append("</td>");

        sb.Append("<td class=\"msg\">").Append(Esc(l.UltimoErroResumido));
        var resumido = l.UltimoErroResumido;
        var completo = l.Mensagem ?? string.Empty;
        var temHistorico = l.Historico is { Count: > 0 };
        if (temHistorico || !string.Equals(resumido, completo, StringComparison.Ordinal))
        {
            sb.Append("<details><summary>Ver detalhe</summary>");
            if (!string.Equals(resumido, completo, StringComparison.Ordinal))
            {
                sb.Append("<div>").Append(Esc(completo)).Append("</div>");
            }

            if (temHistorico)
            {
                sb.Append("<ol>");
                foreach (var item in l.Historico!)
                {
                    sb.Append("<li>").Append(Esc(item)).Append("</li>");
                }

                sb.Append("</ol>");
            }

            sb.Append("</details>");
        }

        sb.Append("</td>");

        sb.Append("<td>").Append(l.EstaIntegrada ? "S" : "N").Append("</td>");
        sb.Append("<td>").Append(l.Tentativas.ToString(CultureInfo.InvariantCulture)).Append("</td>");
        sb.Append("</tr>\n");
    }

    internal static string ChaveStatus(LinhaRelatorioErro l) =>
        l.IntegradaRevisarProduto ? "revisar"
        : l.EstaIntegrada ? "integrada"
        : l.ArquivoIgnorado ? "ignorado"
        : l.StatusEfetivo == StatusLinhaRelatorio.Pendente ? "pendente"
        : "nao-integrada";

    private static string Esc(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        return texto.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string Attr(string? texto) => Esc(texto).Replace("\"", "&quot;");
}
