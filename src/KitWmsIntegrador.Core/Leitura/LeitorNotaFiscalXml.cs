using System.Globalization;
using System.Xml;
using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Leitura;

/// <summary>
/// Leitor/parser de XML de NF-e (layout nacional 4.00) em streaming
/// (<see cref="XmlReader"/>, forward-only), priorizando baixo consumo de memória.
///
/// A comparação de elementos é feita por <see cref="XmlReader.LocalName"/>,
/// ignorando o namespace (<c>http://www.portalfiscal.inf.br/nfe</c>), o que torna
/// o parser robusto ao prefixo/namespace default do layout.
/// </summary>
public sealed class LeitorNotaFiscalXml
{
    private static readonly XmlReaderSettings Settings = new()
    {
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>
    /// Faz o parse de um XML de NF-e a partir de um <see cref="Stream"/>.
    /// </summary>
    public NotaFiscal Parse(Stream xml, string arquivoOrigem)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = XmlReader.Create(xml, Settings);

        string chave = string.Empty;
        string? chaveProt = null;
        string numero = string.Empty;
        string serie = string.Empty;
        string modelo = string.Empty;
        DateTimeOffset? dataEmissao = null;
        string emitCnpj = string.Empty;
        string emitNome = string.Empty;
        string destCnpj = string.Empty;
        string destNome = string.Empty;
        EnderecoDestinatario? endereco = null;
        var itens = new List<ItemNota>();
        decimal? valorTotal = null;
        VolumeTransporte? volumes = null;
        string? transportadoraNome = null;
        string? transportadoraCnpj = null;
        string? infCpl = null;
        string? protocolo = null;

        // Captura campos que preciso preservar fora do lambda por referência.
        var ideNumero = string.Empty;
        var ideSerie = string.Empty;
        var ideModelo = string.Empty;
        DateTimeOffset? ideData = null;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "infNFe":
                    var id = reader.GetAttribute("Id");
                    if (!string.IsNullOrEmpty(id))
                    {
                        chave = id.StartsWith("NFe", StringComparison.OrdinalIgnoreCase)
                            ? id[3..]
                            : id;
                    }
                    break;

                case "ide":
                    (ideNumero, ideSerie, ideModelo, ideData) = LerIde(reader.ReadSubtree());
                    numero = ideNumero;
                    serie = ideSerie;
                    modelo = ideModelo;
                    dataEmissao = ideData;
                    break;

                case "emit":
                    (emitCnpj, emitNome) = LerEmitente(reader.ReadSubtree());
                    break;

                case "dest":
                    (destCnpj, destNome, endereco) = LerDestinatario(reader.ReadSubtree());
                    break;

                case "det":
                    var item = LerDet(reader.ReadSubtree());
                    if (item is not null)
                    {
                        itens.Add(item);
                    }
                    break;

                case "total":
                    valorTotal = LerTotal(reader.ReadSubtree());
                    break;

                case "transp":
                    (volumes, transportadoraNome, transportadoraCnpj) = LerTransp(reader.ReadSubtree());
                    break;

                case "infAdic":
                    infCpl = LerInfCpl(reader.ReadSubtree());
                    break;

                case "chNFe":
                    chaveProt ??= reader.ReadElementContentAsString();
                    break;

                case "nProt":
                    protocolo ??= reader.ReadElementContentAsString();
                    break;
            }
        }

        if (string.IsNullOrEmpty(chave) && !string.IsNullOrEmpty(chaveProt))
        {
            chave = chaveProt;
        }

        return new NotaFiscal
        {
            ChaveNFe = chave,
            Numero = numero,
            Serie = serie,
            Modelo = modelo,
            DataEmissao = dataEmissao,
            EmitenteCnpj = emitCnpj,
            EmitenteNome = emitNome,
            DestinatarioCnpj = destCnpj,
            DestinatarioNome = destNome,
            Endereco = endereco,
            Itens = itens,
            ValorTotal = valorTotal,
            Volumes = volumes,
            TransportadoraNome = transportadoraNome,
            TransportadoraCnpj = transportadoraCnpj,
            Protocolo = protocolo,
            NumeroTransporte = ExtratorTransporte.Extrair(infCpl),
            InfoComplementar = infCpl,
            ArquivoOrigem = arquivoOrigem,
        };
    }

    /// <summary>
    /// Abre o arquivo apenas para leitura (compartilhada) e delega ao <see cref="Parse(Stream, string)"/>.
    /// Nunca escreve, move ou altera o arquivo de origem.
    /// </summary>
    public NotaFiscal ParseArquivo(string caminho)
    {
        using var stream = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Parse(stream, caminho);
    }

    /// <summary>
    /// Itera os elementos filhos diretos de um subtree, invocando
    /// <paramref name="aoEntrarElemento"/> para cada abertura de elemento.
    ///
    /// Trata corretamente a posição do <see cref="XmlReader"/>: o callback pode
    /// consumir o conteúdo do elemento (ex.: <see cref="XmlReader.ReadElementContentAsString"/>
    /// ou <see cref="XmlReader.ReadSubtree"/>), que deixa o reader já posicionado
    /// no próximo nó — por isso o avanço (<c>Read</c>) só ocorre quando o callback
    /// NÃO consumiu o elemento, evitando pular irmãos.
    /// </summary>
    private static void ParaCadaFilho(XmlReader subtree, Func<XmlReader, bool> aoEntrarElemento)
    {
        using (subtree)
        {
            // Posiciona no elemento raiz do subtree.
            if (!subtree.Read())
            {
                return;
            }

            // Lê o primeiro nó interno.
            var avancar = true;
            while (true)
            {
                if (avancar && !subtree.Read())
                {
                    break;
                }

                if (subtree.ReadState != ReadState.Interactive)
                {
                    break;
                }

                if (subtree.NodeType == XmlNodeType.Element)
                {
                    var consumiu = aoEntrarElemento(subtree);
                    // Se o callback consumiu o elemento, o reader já está no
                    // próximo nó: não avançar novamente nesta iteração.
                    avancar = !consumiu;
                }
                else
                {
                    avancar = true;
                }
            }
        }
    }

    private static (string numero, string serie, string modelo, DateTimeOffset? data) LerIde(XmlReader ide)
    {
        string numero = string.Empty, serie = string.Empty, modelo = string.Empty;
        DateTimeOffset? data = null;

        ParaCadaFilho(ide, r =>
        {
            switch (r.LocalName)
            {
                case "nNF": numero = r.ReadElementContentAsString(); return true;
                case "serie": serie = r.ReadElementContentAsString(); return true;
                case "mod": modelo = r.ReadElementContentAsString(); return true;
                case "dhEmi":
                    var raw = r.ReadElementContentAsString();
                    if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        data = dt;
                    }
                    return true;
                default:
                    return false;
            }
        });

        return (numero, serie, modelo, data);
    }

    private static (string cnpj, string nome) LerEmitente(XmlReader emit)
    {
        string cnpj = string.Empty, nome = string.Empty;

        ParaCadaFilho(emit, r =>
        {
            switch (r.LocalName)
            {
                case "CNPJ": cnpj = r.ReadElementContentAsString(); return true;
                case "xNome": nome = r.ReadElementContentAsString(); return true;
                default: return false;
            }
        });

        return (cnpj, nome);
    }

    private static (string cnpj, string nome, EnderecoDestinatario? endereco) LerDestinatario(XmlReader dest)
    {
        string cnpj = string.Empty, nome = string.Empty;
        EnderecoDestinatario? endereco = null;

        ParaCadaFilho(dest, r =>
        {
            switch (r.LocalName)
            {
                case "CNPJ": cnpj = r.ReadElementContentAsString(); return true;
                case "CPF":
                    // Destinatário pessoa física (fallback): usa o CPF como identificador.
                    var cpf = r.ReadElementContentAsString();
                    if (string.IsNullOrEmpty(cnpj))
                    {
                        cnpj = cpf;
                    }
                    return true;
                case "xNome": nome = r.ReadElementContentAsString(); return true;
                case "enderDest": endereco = LerEndereco(r.ReadSubtree()); return true;
                default: return false;
            }
        });

        return (cnpj, nome, endereco);
    }

    private static EnderecoDestinatario LerEndereco(XmlReader ender)
    {
        string? logradouro = null, numero = null, bairro = null, municipio = null, uf = null, cep = null;

        ParaCadaFilho(ender, r =>
        {
            switch (r.LocalName)
            {
                case "xLgr": logradouro = r.ReadElementContentAsString(); return true;
                case "nro": numero = r.ReadElementContentAsString(); return true;
                case "xBairro": bairro = r.ReadElementContentAsString(); return true;
                case "xMun": municipio = r.ReadElementContentAsString(); return true;
                case "UF": uf = r.ReadElementContentAsString(); return true;
                case "CEP": cep = r.ReadElementContentAsString(); return true;
                default: return false;
            }
        });

        return new EnderecoDestinatario
        {
            Logradouro = logradouro,
            Numero = numero,
            Bairro = bairro,
            Municipio = municipio,
            Uf = uf,
            Cep = cep,
        };
    }

    private static ItemNota? LerDet(XmlReader det)
    {
        ItemNota? item = null;

        ParaCadaFilho(det, r =>
        {
            if (r.LocalName == "prod")
            {
                item = LerProd(r.ReadSubtree());
                return true;
            }
            return false;
        });

        return item;
    }

    private static ItemNota LerProd(XmlReader prod)
    {
        string codigo = string.Empty, descricao = string.Empty, unidade = string.Empty, cfop = string.Empty;
        string ncm = string.Empty, ean = string.Empty;
        decimal quantidade = 0m, valor = 0m, valorUnitario = 0m;
        string? lote = null;
        DateOnly? validade = null;

        ParaCadaFilho(prod, r =>
        {
            switch (r.LocalName)
            {
                case "cProd": codigo = r.ReadElementContentAsString(); return true;
                case "xProd": descricao = r.ReadElementContentAsString(); return true;
                case "uCom": unidade = r.ReadElementContentAsString(); return true;
                case "CFOP": cfop = r.ReadElementContentAsString(); return true;
                case "NCM": ncm = r.ReadElementContentAsString(); return true;
                case "cEAN": ean = r.ReadElementContentAsString(); return true;
                case "qCom": quantidade = ParseDecimal(r.ReadElementContentAsString()); return true;
                case "vProd": valor = ParseDecimal(r.ReadElementContentAsString()); return true;
                case "vUnCom": valorUnitario = ParseDecimal(r.ReadElementContentAsString()); return true;
                case "rastro":
                    (lote, validade) = LerRastro(r.ReadSubtree());
                    return true;
                default: return false;
            }
        });

        return new ItemNota
        {
            CodigoProduto = codigo,
            Descricao = descricao,
            Quantidade = quantidade,
            Unidade = unidade,
            ValorProduto = valor,
            ValorUnitario = valorUnitario,
            Cfop = cfop,
            Ncm = ncm,
            Ean = ean,
            Lote = lote,
            Validade = validade,
        };
    }

    private static (string? lote, DateOnly? validade) LerRastro(XmlReader rastro)
    {
        string? lote = null;
        DateOnly? validade = null;

        ParaCadaFilho(rastro, r =>
        {
            switch (r.LocalName)
            {
                case "nLote": lote = r.ReadElementContentAsString(); return true;
                case "dVal":
                    var raw = r.ReadElementContentAsString();
                    if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        validade = d;
                    }
                    return true;
                default: return false;
            }
        });

        return (lote, validade);
    }

    private static decimal? LerTotal(XmlReader total)
    {
        decimal? vNF = null;

        ParaCadaFilho(total, r =>
        {
            if (r.LocalName == "ICMSTot")
            {
                ParaCadaFilho(r.ReadSubtree(), inner =>
                {
                    if (inner.LocalName == "vNF")
                    {
                        vNF = ParseDecimal(inner.ReadElementContentAsString());
                        return true;
                    }
                    return false;
                });
                return true;
            }
            return false;
        });

        return vNF;
    }

    private static (VolumeTransporte? volumes, string? transportadoraNome, string? transportadoraCnpj) LerTransp(XmlReader transp)
    {
        VolumeTransporte? volumes = null;
        string? nome = null, cnpj = null;

        ParaCadaFilho(transp, r =>
        {
            switch (r.LocalName)
            {
                case "transporta":
                    (nome, cnpj) = LerTransporta(r.ReadSubtree());
                    return true;
                case "vol":
                    volumes = LerVolume(r.ReadSubtree());
                    return true;
                default: return false;
            }
        });

        return (volumes, nome, cnpj);
    }

    private static (string? nome, string? cnpj) LerTransporta(XmlReader transporta)
    {
        string? nome = null, cnpj = null;

        ParaCadaFilho(transporta, r =>
        {
            switch (r.LocalName)
            {
                case "xNome": nome = r.ReadElementContentAsString(); return true;
                case "CNPJ": cnpj = r.ReadElementContentAsString(); return true;
                default: return false;
            }
        });

        return (nome, cnpj);
    }

    private static VolumeTransporte LerVolume(XmlReader vol)
    {
        string? qVol = null;
        decimal? pesoL = null, pesoB = null;

        ParaCadaFilho(vol, r =>
        {
            switch (r.LocalName)
            {
                case "qVol": qVol = r.ReadElementContentAsString(); return true;
                case "pesoL": pesoL = ParseDecimalNullable(r.ReadElementContentAsString()); return true;
                case "pesoB": pesoB = ParseDecimalNullable(r.ReadElementContentAsString()); return true;
                default: return false;
            }
        });

        return new VolumeTransporte
        {
            QuantidadeVolumes = qVol,
            PesoLiquido = pesoL,
            PesoBruto = pesoB,
        };
    }

    private static string? LerInfCpl(XmlReader infAdic)
    {
        string? infCpl = null;

        ParaCadaFilho(infAdic, r =>
        {
            if (r.LocalName == "infCpl")
            {
                infCpl = r.ReadElementContentAsString();
                return true;
            }
            return false;
        });

        return infCpl;
    }

    private static decimal ParseDecimal(string? raw)
        => ParseDecimalNullable(raw) ?? 0m;

    private static decimal? ParseDecimalNullable(string? raw)
        => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
