using System.Collections.Concurrent;
using System.Diagnostics;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Construtores.Dtos;

namespace KitWmsIntegrador.Tests.Stress;

/// <summary>Comportamento programado por nota para o fake de carga.</summary>
public enum PlanoFalha
{
    /// <summary>I50 aceita na 1ª tentativa.</summary>
    Sucesso,

    /// <summary>1ª I50 diz produto não cadastrado; I30 ok; 2ª I50 aceita (vai para Processados).</summary>
    ProdutoResolvido,

    /// <summary>1ª I50 diz empresa não localizada; I10 ok; 2ª I50 aceita (vai para Processados).</summary>
    EmpresaResolvida,

    /// <summary>I50 importada e pendente na interface (vai para Processados).</summary>
    Pendencia,

    /// <summary>Produto continua não cadastrado nas 3 tentativas (Erro\ProdutoNaoCadastrado).</summary>
    ProdutoEsgotado,

    /// <summary>Empresa continua não localizada nas 3 tentativas (Erro\EmpresaNaoLocalizada).</summary>
    EmpresaEsgotada,

    /// <summary>Rejeição genérica sem recuperação (Erro\Outro).</summary>
    RejeicaoGenerica,

    /// <summary>"Registro já existe" (idempotência; vai para Processados).</summary>
    JaExiste,
}

/// <summary>
/// Fake thread-safe de <see cref="IClienteWms"/> para carga: SEM rede, com latência simulada
/// configurável e plano de falhas por nota (identificada por <c>PedidoI50.NumeroPedido</c>).
/// Mede chamadas, concorrência máxima observada e a latência média realmente obtida.
/// </summary>
public sealed class FakeClienteWmsCarga : IClienteWms
{
    private readonly ConcurrentDictionary<string, int> _tentativasI50 = new();
    private readonly ConcurrentDictionary<string, PlanoFalha> _plano = new();
    private int _emAndamento;
    private int _maximo;
    private long _chamadas;
    private long _i50;
    private long _i10;
    private long _i30;
    private long _ticksLatencia;

    public FakeClienteWmsCarga(int latenciaMs)
    {
        LatenciaMs = latenciaMs;
    }

    public int LatenciaMs { get; }

    public long Chamadas => Interlocked.Read(ref _chamadas);

    public long ChamadasI50 => Interlocked.Read(ref _i50);

    public long ChamadasI10 => Interlocked.Read(ref _i10);

    public long ChamadasI30 => Interlocked.Read(ref _i30);

    /// <summary>Maior número de chamadas simultâneas observado.</summary>
    public int ConcorrenciaMaxima => Volatile.Read(ref _maximo);

    /// <summary>Latência média efetiva por chamada (ms), medida com Stopwatch (inclui a granularidade do timer).</summary>
    public double LatenciaMediaMedidaMs =>
        Chamadas == 0 ? 0 : TimeSpan.FromTicks(Interlocked.Read(ref _ticksLatencia)).TotalMilliseconds / Chamadas;

    public void Programar(string numeroNota, PlanoFalha plano) => _plano[numeroNota] = plano;

    public async Task<RespostaWms> EnviarAsync(InterfaceWms identificador, object payload, CancellationToken cancellationToken = default)
    {
        var atual = Interlocked.Increment(ref _emAndamento);
        int anterior;
        while (atual > (anterior = Volatile.Read(ref _maximo)))
        {
            Interlocked.CompareExchange(ref _maximo, atual, anterior);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            if (LatenciaMs > 0)
            {
                await Task.Delay(LatenciaMs, cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Increment(ref _chamadas);
            switch (identificador)
            {
                case InterfaceWms.I50:
                    Interlocked.Increment(ref _i50);
                    return ResponderI50(payload);
                case InterfaceWms.I10:
                    Interlocked.Increment(ref _i10);
                    return Resp(identificador, "S", "Registrado com sucesso.");
                case InterfaceWms.I30:
                    Interlocked.Increment(ref _i30);
                    return Resp(identificador, "S", "Registrado com sucesso.");
                default:
                    throw new InvalidOperationException("Interface inesperada no stress: " + identificador);
            }
        }
        finally
        {
            Interlocked.Add(ref _ticksLatencia, sw.Elapsed.Ticks);
            Interlocked.Decrement(ref _emAndamento);
        }
    }

    public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, string jsonBruto, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("O harness de stress só usa o payload tipado.");

    public ResultadoWms Classificar(RetornoWms retorno)
    {
        var mensagem = retorno.Mensagem ?? string.Empty;
        if (retorno.Sucesso.Trim().Equals("S", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoWms.Sucesso;
        }

        return mensagem.Contains("pendente", StringComparison.OrdinalIgnoreCase)
            ? ResultadoWms.Pendente
            : ResultadoWms.Rejeitado;
    }

    private RespostaWms ResponderI50(object payload)
    {
        var nota = payload is PayloadI50 p && p.Pedido.Count > 0
            ? p.Pedido[0].NumeroPedido
            : throw new InvalidOperationException("Payload I50 inesperado no stress.");
        var tentativa = _tentativasI50.AddOrUpdate(nota, 1, (_, n) => n + 1);
        var plano = _plano.TryGetValue(nota, out var pl) ? pl : PlanoFalha.Sucesso;

        return plano switch
        {
            PlanoFalha.ProdutoResolvido when tentativa == 1 => Produto(),
            PlanoFalha.EmpresaResolvida when tentativa == 1 => Empresa(),
            PlanoFalha.Pendencia => Resp(InterfaceWms.I50, "N", "Foi importado e esta pendente na interface."),
            PlanoFalha.ProdutoEsgotado => Produto(),
            PlanoFalha.EmpresaEsgotada => Empresa(),
            PlanoFalha.RejeicaoGenerica => Resp(InterfaceWms.I50, "N", "Falha de validacao generica no WMS."),
            PlanoFalha.JaExiste => Resp(InterfaceWms.I50, "N", "Registro já existe no cadastro."),
            _ => Resp(InterfaceWms.I50, "S", "Registrado com sucesso."),
        };
    }

    private static RespostaWms Produto() => Resp(
        InterfaceWms.I50, "N",
        "Produto Cod\\EAN: \"175886\" da empresa \"3509308\" não ativo ou faltando ou não cadastrado!");

    private static RespostaWms Empresa() => Resp(
        InterfaceWms.I50, "N", "Destinataria - Empresa não localizada, sigla não informada.");

    private static RespostaWms Resp(InterfaceWms identificador, string sucesso, string mensagem) => new()
    {
        TipoEDI = identificador.ToString(),
        Sucesso = sucesso,
        Transacao = "T",
        Retornos = new[] { new RetornoWms { Sucesso = sucesso, Mensagem = mensagem, IdDocumento = "D" } },
    };
}
