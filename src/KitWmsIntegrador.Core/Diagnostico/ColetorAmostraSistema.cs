using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>
/// Uma amostra de saúde: memória do processo do integrador e do servidor. Valores em MB; <c>null</c> = não foi possível
/// medir. Sem linha de comando nem qualquer dado de nota.
/// </summary>
public sealed record AmostraSistema(
    int? ProcessoWsMb, int? ProcessoPrivadoMb, int? Handles, int? Threads,
    int? MemoriaFisicaLivreMb, int? MemoriaFisicaTotalMb, int? PaginacaoUsadaMb, int? PaginacaoTotalMb,
    IReadOnlyList<ConsumidorMemoria> Consumidores);

/// <summary>Coleta uma <see cref="AmostraSistema"/>. A implementação nunca lança.</summary>
public interface IColetorAmostraSistema
{
    /// <summary>Mede o processo e o servidor agora. Falha de uma medida deixa só aquele campo vazio.</summary>
    AmostraSistema Coletar();
}

/// <summary>
/// Implementação real, sem dependências novas: <see cref="Process"/> para o processo e os maiores consumidores,
/// <c>GlobalMemoryStatusEx</c> (kernel32, existe no Windows Server 2012 R2) para memória física e virtual do servidor.
/// A "paginação" é a memória virtual comprometida (commit = RAM + arquivo de paginação): é o número que o alerta
/// de memória virtual baixa do Windows (evento 2004) acompanha. Nunca lança.
/// </summary>
public sealed class ColetorAmostraSistema : IColetorAmostraSistema
{
    /// <summary>Quantos processos entram na lista de maiores consumidores.</summary>
    public const int MaxConsumidores = 5;

    private const long Mb = 1024L * 1024L;

    /// <inheritdoc />
    public AmostraSistema Coletar()
    {
        int? ws = null, privado = null, handles = null, threads = null;
        try
        {
            using var atual = Process.GetCurrentProcess();
            ws = ParaMb(atual.WorkingSet64);
            privado = ParaMb(atual.PrivateMemorySize64);
            handles = atual.HandleCount;
            threads = atual.Threads.Count;
        }
        catch (Exception)
        {
            // medida indisponível: campos ficam vazios
        }

        int? livre = null, total = null, pagUsada = null, pagTotal = null;
        try
        {
            var status = new MemoryStatusEx { Tamanho = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (OperatingSystem.IsWindows() && GlobalMemoryStatusEx(ref status))
            {
                livre = ParaMb(status.FisicaLivre);
                total = ParaMb(status.FisicaTotal);
                pagTotal = ParaMb(status.PaginacaoTotal);
                pagUsada = ParaMb(status.PaginacaoTotal - Math.Min(status.PaginacaoTotal, status.PaginacaoLivre));
            }
        }
        catch (Exception)
        {
            // API indisponível (SO sem kernel32): campos ficam vazios
        }

        return new AmostraSistema(ws, privado, handles, threads, livre, total, pagUsada, pagTotal, MaioresConsumidores());
    }

    /// <summary>Os <see cref="MaxConsumidores"/> nomes de processo com mais memória (working set somado por nome), em MB.</summary>
    private static IReadOnlyList<ConsumidorMemoria> MaioresConsumidores()
    {
        var porNome = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        Process[] todos;
        try
        {
            todos = Process.GetProcesses();
        }
        catch (Exception)
        {
            return Array.Empty<ConsumidorMemoria>();
        }

        foreach (var processo in todos)
        {
            try
            {
                var nome = SanitizadorDiagnostico.Texto(processo.ProcessName, 60);
                if (string.IsNullOrWhiteSpace(nome))
                {
                    continue;
                }

                porNome[nome] = porNome.GetValueOrDefault(nome) + processo.WorkingSet64;
            }
            catch (Exception)
            {
                // processo encerrou durante a leitura ou acesso negado: ignora
            }
            finally
            {
                processo.Dispose();
            }
        }

        return porNome
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxConsumidores)
            .Select(kv => new ConsumidorMemoria(kv.Key, ParaMb(kv.Value)))
            .ToList();
    }

    private static int ParaMb(long bytes) => (int)Math.Min(int.MaxValue, bytes / Mb);

    private static int ParaMb(ulong bytes) => (int)Math.Min(int.MaxValue, bytes / (ulong)Mb);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Tamanho;
        public uint CargaMemoria;
        public ulong FisicaTotal;
        public ulong FisicaLivre;
        public ulong PaginacaoTotal;
        public ulong PaginacaoLivre;
        public ulong VirtualTotal;
        public ulong VirtualLivre;
        public ulong VirtualExtendidaLivre;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
