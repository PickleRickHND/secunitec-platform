namespace Secunitec.BuildingBlocks.Security;

/// <summary>
/// Nombres de las políticas de autorización y la matriz rol → política (docs/PLAN.md §3.2).
/// Es solo datos: el registro en ASP.NET Core vive en <c>Secunitec.BuildingBlocks.AspNetCore</c>.
/// </summary>
public static class SecunitecPolicies
{
    /// <summary>Solo <see cref="SecunitecRoles.Admin"/>.</summary>
    public const string Admin = "secunitec:admin";

    /// <summary>Crear y modificar obligados tributarios.</summary>
    public const string ManageObligados = "secunitec:manage-obligados";

    /// <summary>Crear y modificar clientes del tenant.</summary>
    public const string ManageClientes = "secunitec:manage-clientes";

    /// <summary>Crear, emitir y anular facturas.</summary>
    public const string ManageInvoices = "secunitec:manage-invoices";

    /// <summary>
    /// Leer facturas. El rol <see cref="SecunitecRoles.Cliente"/> queda además restringido por <c>cliente_id</c>
    /// en la capa de aplicación (ABAC).
    /// </summary>
    public const string ReadInvoices = "secunitec:read-invoices";

    /// <summary>Leer la auditoría de seguridad del tenant.</summary>
    public const string ReadAudit = "secunitec:read-audit";

    /// <summary>Roles permitidos por cada política. Cualquier política nueva debe agregarse aquí.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> RolesByPolicy { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Admin] = [SecunitecRoles.Admin],
            [ManageObligados] = [SecunitecRoles.Admin],
            [ManageClientes] = [SecunitecRoles.Admin, SecunitecRoles.Facturador],
            [ManageInvoices] = [SecunitecRoles.Admin, SecunitecRoles.Facturador],
            [ReadInvoices] = [SecunitecRoles.Admin, SecunitecRoles.Facturador, SecunitecRoles.Auditor, SecunitecRoles.Cliente],
            [ReadAudit] = [SecunitecRoles.Admin, SecunitecRoles.Auditor],
        };

    /// <summary>Nombres de todas las políticas registradas.</summary>
    public static IReadOnlyCollection<string> All => RolesByPolicy.Keys.ToArray();
}
