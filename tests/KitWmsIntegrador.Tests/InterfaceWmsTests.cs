using KitWmsIntegrador.Core.ClienteWms;

namespace KitWmsIntegrador.Tests;

public class InterfaceWmsTests
{
    [Theory]
    [InlineData(InterfaceWms.I50, "WEBAPI-I50-Json")]
    [InlineData(InterfaceWms.I60, "WEBAPI-I60-Json")]
    [InlineData(InterfaceWms.I10, "WEBAPI-I10-Json")]
    [InlineData(InterfaceWms.I30, "WEBAPI-I30-Json")]
    public void ParaIdentificador_MapeiaCadaInterface(InterfaceWms interfaceWms, string esperado)
    {
        Assert.Equal(esperado, interfaceWms.ParaIdentificador());
    }
}
