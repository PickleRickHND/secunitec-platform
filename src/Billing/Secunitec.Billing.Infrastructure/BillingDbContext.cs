using Microsoft.EntityFrameworkCore;
using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Infrastructure;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<ObligadoTributario> Obligados => Set<ObligadoTributario>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Factura> Facturas => Set<Factura>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ObligadoTributario>(entity =>
        {
            entity.ToTable("billing_obligados", table =>
            {
                table.HasCheckConstraint("ck_rango", "rango_desde > 0 AND rango_hasta >= rango_desde AND siguiente_correlativo >= rango_desde AND siguiente_correlativo <= rango_hasta + 1");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Rtn).HasColumnName("rtn").HasMaxLength(14).IsRequired();
            entity.HasIndex(x => x.Rtn).IsUnique();
            entity.Property(x => x.RazonSocial).HasColumnName("razon_social").HasMaxLength(250).IsRequired();
            entity.Property(x => x.Cai).HasColumnName("cai").HasMaxLength(37).IsRequired();
            entity.Property(x => x.Prefijo).HasColumnName("prefijo").HasMaxLength(10).IsRequired();
            entity.Property(x => x.RangoDesde).HasColumnName("rango_desde");
            entity.Property(x => x.RangoHasta).HasColumnName("rango_hasta");
            entity.Property(x => x.SiguienteCorrelativo).HasColumnName("siguiente_correlativo");
            entity.Property(x => x.FechaLimiteEmision).HasColumnName("fecha_limite_emision");
            entity.Property(x => x.Activo).HasColumnName("activo");
        });

        modelBuilder.Entity<Cliente>(entity =>
        {
            entity.ToTable("billing_clientes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ObligadoId).HasColumnName("obligado_id");
            entity.HasOne<ObligadoTributario>().WithMany().HasForeignKey(x => x.ObligadoId).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(x => new { x.Id, x.ObligadoId });
            entity.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(250).IsRequired();
            entity.Property(x => x.Rtn).HasColumnName("rtn").HasMaxLength(14);
            entity.Property(x => x.Email).HasColumnName("email").HasMaxLength(250);
        });

        modelBuilder.Entity<Factura>(entity =>
        {
            entity.ToTable("billing_facturas", table =>
            {
                table.HasCheckConstraint("ck_montos", "subtotal >= 0 AND isv >= 0 AND total = subtotal + isv");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ObligadoId).HasColumnName("obligado_id");
            entity.HasOne<ObligadoTributario>().WithMany().HasForeignKey(x => x.ObligadoId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.ClienteId).HasColumnName("cliente_id");
            // R07: FK compuesta impide asociar una factura a un cliente de otro tenant.
            entity.HasOne<Cliente>().WithMany().HasForeignKey(x => new { x.ClienteId, x.ObligadoId })
                .HasPrincipalKey(x => new { x.Id, x.ObligadoId }).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.CreadaPor).HasColumnName("creada_por");
            entity.Property(x => x.Estado).HasColumnName("estado").HasConversion<string>().HasMaxLength(10);
            entity.Property(x => x.Numero).HasColumnName("numero").HasMaxLength(20);
            entity.HasIndex(x => new { x.ObligadoId, x.Numero }).IsUnique();
            entity.Property(x => x.Cai).HasColumnName("cai").HasMaxLength(37);
            entity.Property(x => x.FechaEmision).HasColumnName("fecha_emision");
            entity.Property(x => x.Subtotal).HasColumnName("subtotal").HasPrecision(18, 2);
            entity.Property(x => x.Isv).HasColumnName("isv").HasPrecision(18, 2);
            entity.Property(x => x.Total).HasColumnName("total").HasPrecision(18, 2);
            entity.HasMany(x => x.Lineas).WithOne().HasForeignKey("factura_id").OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Lineas).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<LineaFactura>(entity =>
        {
            entity.ToTable("billing_lineas", table => table.HasCheckConstraint("ck_linea", "cantidad > 0 AND precio_unitario > 0 AND importe >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property<Guid>("factura_id").HasColumnName("factura_id");
            entity.Property(x => x.Descripcion).HasColumnName("descripcion").HasMaxLength(250).IsRequired();
            entity.Property(x => x.Cantidad).HasColumnName("cantidad").HasPrecision(18, 4);
            entity.Property(x => x.PrecioUnitario).HasColumnName("precio_unitario").HasPrecision(18, 2);
            entity.Property(x => x.Exento).HasColumnName("exento");
            entity.Property(x => x.Importe).HasColumnName("importe").HasPrecision(18, 2);
        });
    }
}
