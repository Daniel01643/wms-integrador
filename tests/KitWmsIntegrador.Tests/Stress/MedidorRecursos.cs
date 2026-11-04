using System.Diagnostics;

namespace KitWmsIntegrador.Tests.Stress;

/// <summary>
/// Amostra memória em segundo plano (a cada 200 ms). <c>PeakWorkingSet64</c> é do processo inteiro,
/// por isso cada cenário deve rodar em uma invocação separada do <c>dotnet test</c>.
/// </summary>
public sealed class MedidorRecursos : IDisposable
{
    private readonly Timer _timer;
    private long _picoGerenciado;
    private readonly int _gen0;
    private readonly int _gen1;
    private readonly int _gen2;
    private readonly long _alocadoInicio;

    public MedidorRecursos()
    {
        GC.Collect();
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _alocadoInicio = GC.GetTotalAllocatedBytes(false);
        BaseGerenciadaMb = GC.GetTotalMemory(true) / 1048576.0;
        _timer = new Timer(_ => Amostrar(), null, 0, 200);
    }

    /// <summary>Heap gerenciado (após GC) quando a medição começou.</summary>
    public double BaseGerenciadaMb { get; }

    public double PicoWorkingSetMb { get; private set; }

    public double PicoGerenciadoMb => _picoGerenciado / 1048576.0;

    public double AlocadoTotalMb { get; private set; }

    public int Gen0 { get; private set; }

    public int Gen1 { get; private set; }

    public int Gen2 { get; private set; }

    public void Amostrar()
    {
        var gerenciado = GC.GetTotalMemory(false);
        long anterior;
        while (gerenciado > (anterior = Interlocked.Read(ref _picoGerenciado)))
        {
            Interlocked.CompareExchange(ref _picoGerenciado, gerenciado, anterior);
        }
    }

    /// <summary>Para a amostragem e consolida os números.</summary>
    public void Parar()
    {
        _timer.Dispose();
        Amostrar();
        using var p = Process.GetCurrentProcess();
        PicoWorkingSetMb = p.PeakWorkingSet64 / 1048576.0;
        AlocadoTotalMb = (GC.GetTotalAllocatedBytes(false) - _alocadoInicio) / 1048576.0;
        Gen0 = GC.CollectionCount(0) - _gen0;
        Gen1 = GC.CollectionCount(1) - _gen1;
        Gen2 = GC.CollectionCount(2) - _gen2;
    }

    public void Dispose() => _timer.Dispose();
}
