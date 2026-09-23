using Microsoft.EntityFrameworkCore;
using Npgsql;
using Secunitec.Billing.Application;
using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Infrastructure;

public sealed class EfBillingStore(BillingDbContext db) : IBillingStore
{
    public Task<bool> ExisteObligado(Guid tenantId, CancellationToken cancellationToken) =>
        db.Obligados.AnyAsync(x => x.Id == tenantId, cancellationToken);

    public Task<Cliente?> ObtenerCliente(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ObligadoId == tenantId, cancellationToken);

    public async Task AgregarCliente(Cliente cliente, CancellationToken cancellationToken)
    {
        db.Clientes.Add(cliente);
        await Guardar(cancellationToken);
    }

    public async Task AgregarObligado(ObligadoTributario obligado, CancellationToken cancellationToken)
    {
        db.Obligados.Add(obligado);
        await Guardar(cancellationToken);
    }

    public async Task AgregarFactura(Factura factura, CancellationToken cancellationToken)
    {
        db.Facturas.Add(factura);
        await Guardar(cancellationToken);
    }

    public Task<Factura?> ObtenerFactura(Guid tenantId, Guid id, Guid? clienteId, CancellationToken cancellationToken) =>
        db.Facturas.AsNoTracking().Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == id && x.ObligadoId == tenantId &&
                (clienteId == null || x.ClienteId == clienteId), cancellationToken);

    public async Task<IReadOnlyList<Factura>> ListarFacturas(Guid tenantId, Guid? clienteId, CancellationToken cancellationToken) =>
        await db.Facturas.AsNoTracking().Where(x => x.ObligadoId == tenantId &&
            (clienteId == null || x.ClienteId == clienteId)).OrderByDescending(x => x.FechaEmision)
            .ThenBy(x => x.Id).Take(100).ToListAsync(cancellationToken);

    public async Task<Factura?> Emitir(Guid tenantId, Guid id, DateOnly hoy, CancellationToken cancellationToken)
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // R07: bloquea primero la factura y luego el rango; serializa emisiones concurrentes.
        Factura? factura = await db.Facturas.FromSqlInterpolated(
            $"SELECT * FROM billing_facturas WHERE id = {id} AND obligado_id = {tenantId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (factura is null)
        {
            return null;
        }

        ObligadoTributario obligado = await db.Obligados.FromSqlInterpolated(
            $"SELECT * FROM billing_obligados WHERE id = {tenantId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        factura.Emitir(obligado, hoy);
        await Guardar(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return factura;
    }

    public async Task<Factura?> Anular(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);
        Factura? factura = await db.Facturas.FromSqlInterpolated(
            $"SELECT * FROM billing_facturas WHERE id = {id} AND obligado_id = {tenantId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (factura is null)
        {
            return null;
        }

        factura.Anular();
        await Guardar(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return factura;
    }

    // R18: una restricción única violada (RTN repetido, obligado creado dos veces en paralelo) es un conflicto
    // de negocio y se responde 409; sin esta traducción la API devolvía 500.
    private async Task Guardar(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new BillingConflictException("Ya existe un registro con esos datos.", ex);
        }
    }
}
