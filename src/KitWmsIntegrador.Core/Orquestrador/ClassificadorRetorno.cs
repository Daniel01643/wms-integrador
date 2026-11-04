using KitWmsIntegrador.Core.ClienteWms;

namespace KitWmsIntegrador.Core.Orquestrador;

/// <summary>
/// Classifica o <see cref="RespostaWms"/> a partir do TEXTO real do WMS (não há código de
/// erro estruturado — confirmado no brief e em <c>docs/TESTE-COMUNICACAO.md</c>). Peça pura,
/// SEM estado e SEM rede: o orquestrador a usa para decidir o próximo passo.
///
/// O casamento é case-insensitive por <see cref="string.Contains(string, StringComparison)"/>
/// sobre as mensagens dos <see cref="RetornoWms"/> (e <c>Sucesso=="S"</c> para o caso de
/// sucesso), com os trechos âncora observados ao vivo.
/// </summary>
public sealed class ClassificadorRetorno
{
    private const string TextoSucesso = "Registrado com sucesso";
    private const string TextoEmpresaNaoLocalizada = "Empresa não localizada";
    private const string TextoProdutoNaoCadastrado = "não ativo ou faltando ou não cadastrado";
    private const string TextoPendencia = "pendente na interface";
    private const string TextoJaExiste = "já existe";

    private const StringComparison Comparacao = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Sucesso: <c>Sucesso=="S"</c> na resposta ou em algum retorno, OU alguma mensagem
    /// contém "Registrado com sucesso".
    /// </summary>
    public bool EhSucesso(RespostaWms resposta)
    {
        ArgumentNullException.ThrowIfNull(resposta);

        if (EhS(resposta.Sucesso))
        {
            return true;
        }

        foreach (var retorno in resposta.Retornos)
        {
            if (EhS(retorno.Sucesso) || Contem(retorno.Mensagem, TextoSucesso))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Empresa (destinatário) não localizada no WMS — dispara a pré-condição I10.</summary>
    public bool EhEmpresaNaoLocalizada(RespostaWms resposta) =>
        AlgumaMensagemContem(resposta, TextoEmpresaNaoLocalizada);

    /// <summary>Produto não ativo/cadastrado no WMS — dispara a pré-condição I30.</summary>
    public bool EhProdutoNaoCadastrado(RespostaWms resposta) =>
        AlgumaMensagemContem(resposta, TextoProdutoNaoCadastrado);

    /// <summary>Importado mas pendente na interface do WMS (não é falha definitiva).</summary>
    public bool EhPendencia(RespostaWms resposta) =>
        AlgumaMensagemContem(resposta, TextoPendencia);

    /// <summary>
    /// Pré-condição OK: "já existe" (empresa no I10 / produto no I30) é sucesso efetivo da
    /// pré-condição, ou a própria resposta já é sucesso.
    /// </summary>
    public bool PreCondicaoOk(RespostaWms resposta) =>
        EhSucesso(resposta) || AlgumaMensagemContem(resposta, TextoJaExiste);

    /// <summary>Primeira mensagem não-vazia dos retornos (para relatório/log). "" se não houver.</summary>
    public string PrimeiraMensagem(RespostaWms resposta)
    {
        ArgumentNullException.ThrowIfNull(resposta);

        foreach (var retorno in resposta.Retornos)
        {
            if (!string.IsNullOrWhiteSpace(retorno.Mensagem))
            {
                return retorno.Mensagem;
            }
        }

        return string.Empty;
    }

    private static bool AlgumaMensagemContem(RespostaWms resposta, string trecho)
    {
        ArgumentNullException.ThrowIfNull(resposta);

        foreach (var retorno in resposta.Retornos)
        {
            if (Contem(retorno.Mensagem, trecho))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contem(string? texto, string trecho) =>
        !string.IsNullOrEmpty(texto) && texto.Contains(trecho, Comparacao);

    private static bool EhS(string? valor) =>
        !string.IsNullOrEmpty(valor) && valor.Trim().Equals("S", Comparacao);
}
