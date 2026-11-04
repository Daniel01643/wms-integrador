using KitWmsIntegrador.Core.ClienteWms;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>
/// Fake de <see cref="IClienteWms"/> para os testes do Orquestrador. NÃO faz rede:
/// devolve respostas programadas por <see cref="InterfaceWms"/> e grava cada envio
/// (interface + payload) para asserts de ordem/conteúdo.
///
/// Para cada interface, configure uma fila de respostas (<see cref="Responder"/>).
/// Quando a fila esgota, repete a última resposta configurada — útil para simular um
/// erro persistente (ex.: produto que continua não cadastrado nas 3 tentativas).
/// </summary>
public sealed class FakeClienteWms : IClienteWms
{
    private readonly Dictionary<InterfaceWms, Queue<RespostaWms>> _filas = new();
    private readonly Dictionary<InterfaceWms, RespostaWms> _ultima = new();

    /// <summary>Envios gravados na ordem em que ocorreram.</summary>
    public List<EnvioRegistrado> Envios { get; } = new();

    /// <summary>Programa a sequência de respostas devolvidas para a interface indicada.</summary>
    public FakeClienteWms Responder(InterfaceWms identificador, params RespostaWms[] respostas)
    {
        _filas[identificador] = new Queue<RespostaWms>(respostas);
        if (respostas.Length > 0)
        {
            _ultima[identificador] = respostas[^1];
        }

        return this;
    }

    /// <summary>Quantidade de envios registrados para uma interface específica.</summary>
    public int Contar(InterfaceWms identificador) => Envios.Count(e => e.Identificador == identificador);

    public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, object payload, CancellationToken cancellationToken = default)
    {
        Envios.Add(new EnvioRegistrado(identificador, payload));
        return Task.FromResult(ProximaResposta(identificador));
    }

    public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, string jsonBruto, CancellationToken cancellationToken = default)
    {
        Envios.Add(new EnvioRegistrado(identificador, jsonBruto));
        return Task.FromResult(ProximaResposta(identificador));
    }

    public ResultadoWms Classificar(RetornoWms retorno)
    {
        // Helper simples por texto — não é a regra de negócio (o Orquestrador usa o
        // ClassificadorRetorno real). Mantém compatibilidade com a interface.
        var mensagem = retorno.Mensagem ?? string.Empty;
        if (retorno.Sucesso.Trim().Equals("S", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoWms.Sucesso;
        }

        return mensagem.Contains("pendente", StringComparison.OrdinalIgnoreCase)
            ? ResultadoWms.Pendente
            : ResultadoWms.Rejeitado;
    }

    private RespostaWms ProximaResposta(InterfaceWms identificador)
    {
        if (_filas.TryGetValue(identificador, out var fila) && fila.Count > 0)
        {
            return fila.Dequeue();
        }

        if (_ultima.TryGetValue(identificador, out var ultima))
        {
            return ultima;
        }

        throw new InvalidOperationException(
            $"FakeClienteWms sem resposta programada para {identificador}.");
    }

    /// <summary>Registro de um envio: a interface e o objeto/JSON enviado.</summary>
    public sealed record EnvioRegistrado(InterfaceWms Identificador, object Payload);
}
