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
.topo{display:flex;align-items:center;gap:18px;border-bottom:4px solid #17479E;padding-bottom:12px;margin-bottom:16px}
.topo img{min-width:3.5cm;max-height:70px;margin:6px;display:block}
.marca{font-size:30px;font-weight:bold;color:#17479E;letter-spacing:.5px;padding:6px 10px}
h1{font-size:22px;margin:0;color:#17479E}
h2{font-size:16px;margin:22px 0 0;color:#17479E}
.barra{display:flex;width:64px;height:4px;margin:6px 0 12px}
.barra i{display:block;flex:1}
.barra i:nth-child(1){background:#17479E}.barra i:nth-child(2){background:#54B6E7}.barra i:nth-child(3){background:#009A83}
.gerado{color:#333333;font-size:12px;margin:2px 0 0}
.cards{display:flex;flex-wrap:wrap;gap:10px;margin:8px 0}
.card{border:1px solid #C1C1C1;border-top:4px solid #54B6E7;border-radius:4px;padding:8px 14px;min-width:130px}
.card b{display:block;font-size:24px;color:#17479E}
.card.ok{border-top-color:#009A83}.card.ok b{color:#009A83}
.card.rev{border-top-color:#009A83}
.card.pen{border-top-color:#F5A623}.card.pen b{color:#B8741A}
.card.nao{border-top-color:#D32F2F}.card.nao b{color:#D32F2F}
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
.revmarca{display:block;margin-top:3px;font-size:11px;color:#17479E}
.id{white-space:nowrap}
.msg{min-width:280px}
details{margin-top:4px}summary{cursor:pointer;color:#17479E}
details div{margin:4px 0;white-space:pre-wrap;word-break:break-word}
details ol{margin:4px 0 0 18px;padding:0;font-size:12px}
.controles{display:flex;gap:10px;align-items:center;margin:10px 0}
.controles input,.controles select{padding:5px 8px;border:1px solid #C1C1C1;border-radius:3px;font-family:Arial,Helvetica,sans-serif}
.nota{color:#333333;font-size:12px}
@media print{body{padding:0}thead th{position:static}.controles{display:none}details>*{display:block}}
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
if(ok){vis++;cont[l.getAttribute('data-transporte')]=true;}
});
grupos.forEach(function(g){g.hidden=!cont[g.getAttribute('data-transporte')];});
n.textContent='Mostrando '+vis+' de '+linhas.length;
}
q.addEventListener('input',aplicar);
s.addEventListener('change',aplicar);
aplicar();
})();
";

    public static string Renderizar(
        ModeloRelatorio modelo, DateTimeOffset geradoEm, byte[]? logoPng, PeriodoColeta? periodo = null)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.Append("<title>").Append(Esc(IdentidadeVisual.Titulo)).Append("</title>");
        sb.Append("<style>").Append(Css).Append("</style></head><body>\n");

        // Cabeçalho: logo (base64) ou marca em texto.
        sb.Append("<div class=\"topo\">");
        if (logoPng is not null)
        {
            sb.Append("<img alt=\"Friozem\" src=\"data:image/png;base64,")
              .Append(Convert.ToBase64String(logoPng)).Append("\" />");
        }
        else
        {
            sb.Append("<span class=\"marca\">Friozem</span>");
        }

        sb.Append("<div><h1>").Append(Esc(IdentidadeVisual.Titulo)).Append("</h1>");
        sb.Append(Barra());
        sb.Append("<p class=\"gerado\">Gerado em: ")
          .Append(Esc(geradoEm.ToString(IdentidadeVisual.FormatoHorario, CultureInfo.InvariantCulture) + TextoColeta(periodo)))
          .Append("</p></div></div>\n");

        AppendResumo(sb, modelo.Resumo);
        AppendProdutosRevisao(sb, modelo);
        AppendTabela(sb, modelo);

        sb.Append("<script>").Append(Script).Append("</script>\n");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Complemento do cabeçalho: ' | Coleta: primeira a última gravação' (vazio sem período).</summary>
    internal static string TextoColeta(PeriodoColeta? periodo) =>
        periodo is null
            ? string.Empty
            : " | Coleta: " + periodo.PrimeiraGravacao.ToString(IdentidadeVisual.FormatoHorario, CultureInfo.InvariantCulture)
              + " a " + periodo.UltimaGravacao.ToString(IdentidadeVisual.FormatoHorario, CultureInfo.InvariantCulture);

    private static string Barra() => "<div class=\"barra\"><i></i><i></i><i></i></div>";

    private static void AppendResumo(StringBuilder sb, ResumoRelatorio r)
    {
        sb.Append("<h2>Resumo</h2>").Append(Barra());
        sb.Append("<div class=\"cards\">");
        Card(sb, "", "Notas processadas", r.Total);
        Card(sb, "ok", "Integradas", r.Integradas);
        Card(sb, "rev", "Integradas — revisar produto", r.IntegradasRevisarProduto);
        Card(sb, "pen", "Pendentes", r.Pendentes);
        Card(sb, "nao", "Não integradas", r.NaoIntegradas);
        Card(sb, "", "Transportes", r.Transportes);
        sb.Append("</div>");
        sb.Append("<p class=\"porte\"><b>Notas por transporte:</b> ");
        foreach (var (transporte, notas) in r.NotasPorTransporte)
        {
            sb.Append("<span class=\"chip\">").Append(Esc(transporte)).Append(": ")
              .Append(Esc(IdentidadeVisual.Plural(notas, "nota", "notas"))).Append("</span>");
        }

        sb.Append("</p>\n");
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
          .Append("</select><span id=\"contagem\"></span></div>\n");

        sb.Append("<table><thead><tr>");
        foreach (var h in new[]
                 {
                     "Nota", "Transporte", "Horário", "Status", "ID de processamento",
                     "Mensagem", "Integrada", "Tentativas",
                 })
        {
            sb.Append("<th>").Append(Esc(h)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>\n");

        foreach (var g in modelo.Grupos)
        {
            sb.Append("<tr class=\"grupo\" data-transporte=\"").Append(Attr(g.Transporte)).Append("\"><td colspan=\"8\">")
              .Append("Transporte ").Append(Esc(g.Transporte)).Append(" — ")
              .Append(Esc(IdentidadeVisual.Plural(g.Notas, "nota", "notas"))).Append(" · ")
              .Append(Esc(IdentidadeVisual.Plural(g.Integradas, "integrada", "integradas"))).Append(" · ")
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
        var texto = $"{l.NumeroNota} {l.NumeroTransporte} {l.Mensagem} {l.IdTransacao} {l.IdDocumento}".ToLowerInvariant();

        sb.Append("<tr data-status=\"").Append(chave)
          .Append("\" data-transporte=\"").Append(Attr(l.NumeroTransporte))
          .Append("\" data-nota=\"").Append(Attr(l.NumeroNota))
          .Append("\" data-texto=\"").Append(Attr(texto)).Append("\">");
        sb.Append("<td>").Append(Esc(l.NumeroNota)).Append("</td>");
        sb.Append("<td>").Append(Esc(l.NumeroTransporte)).Append("</td>");
        sb.Append("<td>").Append(Esc(l.HorarioErro.ToString(IdentidadeVisual.FormatoHorario, CultureInfo.InvariantCulture))).Append("</td>");

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
