using KitWmsIntegrador.Core.Diagnostico;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>Diário de diagnóstico em memória para os testes de instrumentação.</summary>
public sealed class RegistroDiagnosticoMemoria : IRegistroDiagnostico
{
    private readonly List<EventoDiagnostico> _eventos = new();

    /// <summary>Cópia dos eventos gravados, na ordem.</summary>
    public IReadOnlyList<EventoDiagnostico> Eventos
    {
        get
        {
            lock (_eventos)
            {
                return _eventos.ToList();
            }
        }
    }

    public void Registrar(EventoDiagnostico evento)
    {
        lock (_eventos)
        {
            _eventos.Add(evento);
        }
    }

    public IReadOnlyList<EventoDiagnostico> De(string tipo) => Eventos.Where(e => e.Tipo == tipo).ToList();
}
