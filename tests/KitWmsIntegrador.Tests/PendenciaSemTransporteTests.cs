using System.Text;
using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.Leitura;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Cobre o caminho ponta-a-ponta (parse -> agrupamento) de um XML de NF-e que NÃO
/// contém o padrão "Transporte:" no infCpl — situação esperada para emitentes
/// não-BRF. A nota deve ser parseada sem exceção e cair em <c>SemTransporte</c>
/// (pendência), sem quebrar a varredura. O escopo atual é BRF (que sempre tem o
/// transporte); este teste garante que o fallback de pendência funciona quando o
/// campo não existir, usando um XML sintético válido (sem amostra não-BRF real).
/// </summary>
public class PendenciaSemTransporteTests
{
    // XML mínimo de NF-e (layout nacional 4.00, namespace oficial) com infCpl SEM
    // o padrão "Transporte:".
    private const string XmlSemTransporte =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
          <NFe>
            <infNFe Id="NFe35251299999999999999550010000000011000000011" versao="4.00">
              <ide>
                <nNF>99999999</nNF>
                <serie>1</serie>
                <mod>55</mod>
                <dhEmi>2025-12-15T08:00:00-03:00</dhEmi>
              </ide>
              <emit>
                <CNPJ>99999999000199</CNPJ>
                <xNome>EMITENTE NAO BRF LTDA</xNome>
              </emit>
              <dest>
                <CNPJ>11111111000111</CNPJ>
                <xNome>DESTINATARIO TESTE</xNome>
              </dest>
              <det nItem="1">
                <prod>
                  <cProd>ABC123</cProd>
                  <xProd>PRODUTO TESTE</xProd>
                  <qCom>1.0000</qCom>
                  <uCom>UN</uCom>
                  <vProd>10.00</vProd>
                  <CFOP>5102</CFOP>
                </prod>
              </det>
              <total>
                <ICMSTot>
                  <vNF>10.00</vNF>
                </ICMSTot>
              </total>
              <infAdic>
                <infCpl>Pedido do cliente sem numero de transporte informado.</infCpl>
              </infAdic>
            </infNFe>
          </NFe>
        </nfeProc>
        """;

    [Fact]
    public void ParseEAgrupar_XmlSemPadraoTransporte_CaiEmPendencias()
    {
        var leitor = new LeitorNotaFiscalXml();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(XmlSemTransporte));

        var nota = leitor.Parse(stream, "sintetico-sem-transporte.xml");

        // Parse não lança e extrai os campos; só o transporte fica nulo.
        Assert.Equal("99999999", nota.Numero);
        Assert.Equal("35251299999999999999550010000000011000000011", nota.ChaveNFe);
        Assert.Null(nota.NumeroTransporte);

        var resultado = new AgrupadorTransporte().Agrupar(new[] { nota });

        Assert.Equal(1, resultado.TotalNotas);
        Assert.Empty(resultado.Ondas);
        Assert.Single(resultado.SemTransporte);
        Assert.Same(nota, resultado.SemTransporte[0]);
    }
}
