namespace KitWmsIntegrador.Core.Pacote;

/// <summary>Categoria de erro de uma nota; define a subpasta em <c>Erro\&lt;Categoria&gt;</c>.</summary>
public enum CategoriaErro
{
    /// <summary>XML não pôde ser lido/validado.</summary>
    XmlInvalido,

    /// <summary>Arquivo cuja extensão não é .xml — ignorado pela integração.</summary>
    ExtensaoInvalida,

    /// <summary>Nota sem número de transporte.</summary>
    SemTransporte,

    /// <summary>Produto não cadastrado/ativo no WMS.</summary>
    ProdutoNaoCadastrado,

    /// <summary>Empresa (destinatário) não localizada no WMS.</summary>
    EmpresaNaoLocalizada,

    /// <summary>Esgotou as tentativas sem sucesso.</summary>
    TentativasEsgotadas,

    /// <summary>Qualquer outro erro.</summary>
    Outro,

    /// <summary>ZIP corrompido, protegido, grande demais, sem XML ou com entrada de caminho perigoso.</summary>
    ZipInvalido,
}

/// <summary>Destino de um arquivo após o processamento.</summary>
public enum DestinoArquivo
{
    /// <summary>Pasta Processados.</summary>
    Processados,

    /// <summary>Pasta Erro.</summary>
    Erro,
}
