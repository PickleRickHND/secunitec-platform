using Microsoft.EntityFrameworkCore;
using Npgsql;
using Secunitec.Billing.Application;
using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Infrastructure;

// Adaptadores EF Core de los puertos de Billing.Application. Todas las consultas pasan por el filtro global del
// tenant (BillingDbContext); las de bloqueo repiten el tenant en el SQL, que el filtro no alcanza a reescribir.

public sealed class EfFacturaRepository(BillingDbContext db) : IFacturaRepository
{
    public Task<Factura?> Obtener(Guid id, CancellationToken cancellationToken) =>
        db.Facturas.Include(x => x.Lineas).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Factura?> ObtenerParaActualizar(Guid id, CancellationToken cancellationToken) =>
        db.Facturas
            .FromSqlInterpolated($"SELECT * FROM billing_facturas WHERE id = {id} AND obligado_id = {db.TenantActual} FOR UPDATE")
            .Include(x => x.Lineas)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> ExisteFueraDeAlcance(Guid id, CancellationToken cancellationToken) =>
        db.Facturas.IgnoreQueryFilters().AnyAsync(x => x.Id == id, cancellationToken);

    public async Task<Pagina<FacturaResumen>> Listar(FiltroFacturas filtro, CancellationToken cancellationToken)
    {
        IQueryable<Factura> query = db.Facturas.AsNoTracking();
        if (filtro.Estado is EstadoFactura estado)
        {
            query = query.Where(x => x.Estado == estado);
        }

        if (filtro.ClienteId is Guid clienteId)
        {
            query = query.Where(x => x.ClienteId == clienteId);
        }

        // Las fechas filtran por la fecha fiscal de emisión: un borrador no tiene y queda fuera.
        if (filtro.Desde is DateOnly desde)
        {
            query = query.Where(x => x.FechaEmision >= desde);
        }

        if (filtro.Hasta is DateOnly hasta)
        {
            query = query.Where(x => x.FechaEmision <= hasta);
        }

        int total = await query.CountAsync(cancellationToken);
        var filas = await query
            .Join(db.Clientes, factura => factura.ClienteId, cliente => cliente.Id, (factura, cliente) => new
            {
                factura.Id,
                factura.ClienteId,
                cliente.Nombre,
                factura.Numero,
                factura.Estado,
                factura.FechaEmision,
                factura.CreadaEn,
                factura.Subtotal,
                factura.Isv,
                factura.Total,
            })
            .OrderByDescending(x => x.CreadaEn).ThenBy(x => x.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamano).Take(filtro.Tamano)
            .ToListAsync(cancellationToken);

        return new Pagina<FacturaResumen>(
            filas.Select(x => new FacturaResumen(x.Id, x.ClienteId, x.Nombre, x.Numero, x.Estado, x.FechaEmision, x.CreadaEn,
                x.Subtotal.Amount, x.Isv.Amount, x.Total.Amount)).ToArray(),
            total, filtro.Pagina, filtro.Tamano);
    }

    public void Agregar(Factura factura) => db.Facturas.Add(factura);
}

public sealed class EfClienteRepository(BillingDbContext db) : IClienteRepository
{
    public Task<Cliente?> Obtener(Guid id, CancellationToken cancellationToken) =>
        db.Clientes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExisteFueraDeAlcance(Guid id, CancellationToken cancellationToken) =>
        db.Clientes.IgnoreQueryFilters().AnyAsync(x => x.Id == id, cancellationToken);

    public async Task<Pagina<ClienteDto>> Listar(FiltroClientes filtro, CancellationToken cancellationToken)
    {
        IQueryable<Cliente> query = db.Clientes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            string buscar = filtro.Buscar.Trim();
            string patron = "%" + buscar.Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal) + "%";
            Rtn? rtn = buscar.Length == 14 && buscar.All(char.IsAsciiDigit) ? new Rtn(buscar) : null;
            query = query.Where(x => EF.Functions.ILike(x.Nombre, patron, @"\") ||
                (x.Email != null && EF.Functions.ILike(x.Email, patron, @"\")) ||
                (rtn != null && x.Rtn == rtn));
        }

        int total = await query.CountAsync(cancellationToken);
        List<Cliente> clientes = await query.OrderBy(x => x.Nombre).ThenBy(x => x.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamano).Take(filtro.Tamano).ToListAsync(cancellationToken);

        return new Pagina<ClienteDto>(
            clientes.Select(x => new ClienteDto(x.Id, x.Nombre, x.Rtn?.Value, x.Email)).ToArray(),
            total, filtro.Pagina, filtro.Tamano);
    }

    public void Agregar(Cliente cliente) => db.Clientes.Add(cliente);
}

public sealed class EfObligadoRepository(BillingDbContext db) : IObligadoRepository, INumeradorFacturas
{
    public Task<ObligadoTributario?> ObtenerActual(CancellationToken cancellationToken) =>
        db.Obligados.FirstOrDefaultAsync(x => x.Id == db.TenantActual, cancellationToken);

    public Task<ObligadoTributario?> ObtenerActualParaActualizar(CancellationToken cancellationToken) =>
        db.Obligados
            .FromSqlInterpolated($"SELECT * FROM billing_obligados WHERE id = {db.TenantActual} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    // R07: el bloqueo de la fila del obligado (que guarda el siguiente correlativo) serializa las emisiones.
    public async Task<ObligadoTributario> BloquearRango(CancellationToken cancellationToken) =>
        await ObtenerActualParaActualizar(cancellationToken)
            ?? throw new BillingRuleException(BillingErrorCodes.ObligadoRequerido, "Registre primero al obligado tributario.");

    public void Agregar(ObligadoTributario obligado) => db.Obligados.Add(obligado);
}

public sealed class EfUnitOfWork(BillingDbContext db) : IUnitOfWork
{
    public async Task<T> EnTransaccion<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        T resultado = await operacion(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        // R18: una restricción única violada (RTN repetido, alta concurrente, rango de CAI que repite números) es un
        // conflicto de negocio: 409, no 500.
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new BillingConflictException("Ya existe un registro con esos datos.", ex);
        }

        await transaction.CommitAsync(cancellationToken);
        return resultado;
    }
}
