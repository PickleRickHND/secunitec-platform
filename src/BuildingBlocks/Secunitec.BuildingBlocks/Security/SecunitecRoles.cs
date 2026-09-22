namespace Secunitec.BuildingBlocks.Security;

/// <summary>Roles RBAC del sistema (docs/PLAN.md §3.2). Los valores viajan tal cual en el claim <c>role</c>.</summary>
public static class SecunitecRoles
{
    /// <summary>Gestiona obligados, usuarios y roles de su tenant; incluye todo lo de los demás roles.</summary>
    public const string Admin = "Admin";

    /// <summary>Crea, emite y anula facturas; gestiona clientes de su tenant.</summary>
    public const string Facturador = "Facturador";

    /// <summary>Lee facturas y auditoría de su tenant. No escribe.</summary>
    public const string Auditor = "Auditor";

    /// <summary>Lee únicamente sus propias facturas (ABAC por <c>cliente_id</c>).</summary>
    public const string Cliente = "Cliente";

    /// <summary>Todos los roles conocidos, en orden de privilegio descendente.</summary>
    public static IReadOnlyList<string> All { get; } = [Admin, Facturador, Auditor, Cliente];

    /// <summary>Indica si <paramref name="role"/> es uno de los roles del contrato (comparación exacta).</summary>
    public static bool IsKnown(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);
}
