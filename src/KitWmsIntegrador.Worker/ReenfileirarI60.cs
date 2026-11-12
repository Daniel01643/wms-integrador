using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// CLI <c>--reenfileirar-i60 --chave &lt;chave&gt;</c> ou <c>--motivo &lt;MotivoI60&gt;</c>. NÃO edita as notas: grava um
/// pedido em <c>Estado\i51\comandos</c> que o Worker em execução aplica no ciclo seguinte (ele é a única escritora do
/// estado). Saída: 0 gravado, 1 erro de disco, 2 parâmetro inválido.
/// </summary>
public static class ReenfileirarI60
{
    /// <summary>Argumento que aciona a CLI.</summary>
    public const string Argumento = "--reenfileirar-i60";

    /// <summary>Executa a CLI com a configuração do Worker.</summary>
    public static Task<int> ExecutarAsync(string[] args)
    {
        var config = ConfiguracaoManual.Criar();
        var caminhos = new CaminhosI51(Options.Create(ConfiguracaoManual.Pastas(config)));
        var comandos = new ComandosI51(caminhos, new SystemClock(), NullLogger<ComandosI51>.Instance);
        return Task.FromResult(Executar(args, comandos, Console.Out));
    }

    /// <summary>Executa a lógica (testável): valida parâmetros e grava o pedido.</summary>
    public static int Executar(string[] args, ComandosI51 comandos, TextWriter saida)
    {
        var chave = ConfiguracaoManual.Argumento(args, "--chave");
        var motivo = ConfiguracaoManual.Argumento(args, "--motivo");
        if (string.IsNullOrWhiteSpace(chave) && string.IsNullOrWhiteSpace(motivo))
        {
            saida.WriteLine("Informe --chave <chave da nota> ou --motivo <MotivoI60>.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(chave))
        {
            var erro = ComandosI51.ValidarMotivo(motivo);
            if (erro is not null)
            {
                saida.WriteLine(erro);
                return 2;
            }
        }

        if (!comandos.GravarReenfileirar(chave, motivo))
        {
            saida.WriteLine("Não foi possível gravar o pedido de reenfileiramento (erro de disco).");
            return 1;
        }

        saida.WriteLine("Pedido gravado. O Worker em execução reenfileira as notas rejeitadas no próximo ciclo da I51.");
        return 0;
    }
}
