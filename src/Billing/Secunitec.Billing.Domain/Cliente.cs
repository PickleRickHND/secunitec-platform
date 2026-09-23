namespace Secunitec.Billing.Domain;

public sealed class Cliente
{
    private Cliente() { }

    public Cliente(Guid id, Guid obligadoId, string nombre, string? rtn, string? email)
    {
        if (id == Guid.Empty || obligadoId == Guid.Empty || string.IsNullOrWhiteSpace(nombre))
        {
            throw new BillingRuleException("Los datos del cliente son inválidos.");
        }

        Id = id;
        ObligadoId = obligadoId;
        Nombre = nombre.Trim();
        Rtn = rtn is null ? null : TaxIdentity.Rtn(rtn);
        Email = email;
    }

    public Guid Id { get; private set; }
    public Guid ObligadoId { get; private set; }
    public string Nombre { get; private set; } = "";
    public string? Rtn { get; private set; }
    public string? Email { get; private set; }
}
