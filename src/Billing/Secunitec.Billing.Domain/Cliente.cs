namespace Secunitec.Billing.Domain;

/// <summary>Cliente de un obligado; pertenece a un solo tenant.</summary>
public sealed class Cliente
{
    public const int MaxTexto = 250;

    private Cliente() { }

    public Cliente(Guid id, Guid obligadoId, string nombre, Rtn? rtn, string? email)
    {
        if (id == Guid.Empty || obligadoId == Guid.Empty)
        {
            throw new BillingRuleException(BillingErrorCodes.ClienteInvalido, "El cliente necesita identificador y obligado.");
        }

        Id = id;
        ObligadoId = obligadoId;
        Actualizar(nombre, rtn, email);
    }

    public Guid Id { get; private set; }
    public Guid ObligadoId { get; private set; }
    public string Nombre { get; private set; } = "";
    public Rtn? Rtn { get; private set; }
    public string? Email { get; private set; }

    public void Actualizar(string nombre, Rtn? rtn, string? email)
    {
        // Los límites replican varchar(250) de la BD: un texto más largo sería un 500 en vez de un 400 (R18).
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > MaxTexto || email?.Trim().Length > MaxTexto)
        {
            throw new BillingRuleException(BillingErrorCodes.ClienteInvalido, "Los datos del cliente son inválidos.");
        }

        Nombre = nombre.Trim();
        Rtn = rtn;
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }
}
