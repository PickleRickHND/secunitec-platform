using Microsoft.EntityFrameworkCore.Migrations;

namespace Secunitec.Billing.Infrastructure.Migrations;

/// <summary>
/// InitialBilling (etapa 2.2) creó el esquema con SQL escrito a mano y nombres propios de restricciones e índices.
/// El snapshot asume los nombres por convención de EF (PK_*, AK_*, FK_*, IX_*): sin esta alineación, cualquier
/// migración futura que toque esos objetos fallaría. No cambia datos ni columnas. Cada paso comprueba el nombre
/// viejo para que sea idempotente.
/// </summary>
public partial class AlinearEsquemaBilling : Migration
{
    // (tabla, nombre en InitialBilling, nombre por convención de EF). Renombrar una PK, AK o UNIQUE renombra
    // también su índice.
    private static readonly (string Table, string Old, string New)[] _constraints =
    [
        ("billing_obligados", "billing_obligados_pkey", "PK_billing_obligados"),
        ("billing_clientes", "billing_clientes_pkey", "PK_billing_clientes"),
        ("billing_facturas", "billing_facturas_pkey", "PK_billing_facturas"),
        ("billing_lineas", "billing_lineas_pkey", "PK_billing_lineas"),
        ("billing_clientes", "ak_cliente_tenant", "AK_billing_clientes_id_obligado_id"),
        ("billing_clientes", "billing_clientes_obligado_id_fkey", "FK_billing_clientes_billing_obligados_obligado_id"),
        ("billing_facturas", "billing_facturas_obligado_id_fkey", "FK_billing_facturas_billing_obligados_obligado_id"),
        ("billing_facturas", "fk_factura_cliente_tenant", "FK_billing_facturas_billing_clientes_cliente_id_obligado_id"),
        ("billing_lineas", "billing_lineas_factura_id_fkey", "FK_billing_lineas_billing_facturas_factura_id"),
    ];

    private static readonly (string Old, string New)[] _indexes =
    [
        ("ix_facturas_cliente_tenant", "IX_billing_facturas_cliente_id_obligado_id"),
        ("ix_lineas_factura", "IX_billing_lineas_factura_id"),
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach ((string table, string old, string @new) in _constraints)
        {
            migrationBuilder.Sql(RenameConstraint(table, old, @new));
        }

        foreach ((string old, string @new) in _indexes)
        {
            migrationBuilder.Sql($"ALTER INDEX IF EXISTS {old} RENAME TO \"{@new}\";");
        }

        // EF modela los RTN y los correlativos únicos como índices únicos, no como restricciones UNIQUE.
        migrationBuilder.Sql("""
            ALTER TABLE billing_obligados DROP CONSTRAINT IF EXISTS billing_obligados_rtn_key;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_billing_obligados_rtn" ON billing_obligados (rtn);
            ALTER TABLE billing_facturas DROP CONSTRAINT IF EXISTS uq_factura_numero;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_billing_facturas_obligado_id_numero" ON billing_facturas (obligado_id, numero);
            CREATE INDEX IF NOT EXISTS "IX_billing_clientes_obligado_id" ON billing_clientes (obligado_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_billing_clientes_obligado_id";
            DROP INDEX IF EXISTS "IX_billing_facturas_obligado_id_numero";
            ALTER TABLE billing_facturas ADD CONSTRAINT uq_factura_numero UNIQUE (obligado_id, numero);
            DROP INDEX IF EXISTS "IX_billing_obligados_rtn";
            ALTER TABLE billing_obligados ADD CONSTRAINT billing_obligados_rtn_key UNIQUE (rtn);
            """);

        foreach ((string old, string @new) in _indexes)
        {
            migrationBuilder.Sql($"ALTER INDEX IF EXISTS \"{@new}\" RENAME TO {old};");
        }

        foreach ((string table, string old, string @new) in _constraints)
        {
            migrationBuilder.Sql(RenameConstraint(table, @new, old));
        }
    }

    private static string RenameConstraint(string table, string from, string to) => $"""
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = '{from}' AND conrelid = '{table}'::regclass) THEN
                ALTER TABLE {table} RENAME CONSTRAINT "{from}" TO "{to}";
            END IF;
        END $$;
        """;
}
