namespace Secunitec.Billing.Domain;

public enum EstadoFactura { Borrador, Emitida, Anulada }

public sealed class LineaFactura
{
    private LineaFactura() { }

    public LineaFactura(string descripcion, decimal cantidad, decimal precioUnitario, bool exento)
    {
        if (string.IsNullOrWhiteSpace(descripcion) || descripcion.Length > 250 ||
            cantidad <= 0 || cantidad > 999_999_999m || precioUnitario <= 0 || precioUnitario > 999_999_999m ||
            decimal.Round(precioUnitario, 2) != precioUnitario ||
            decimal.Round(cantidad, 4) != cantidad)
        {
            throw new BillingRuleException("La línea debe tener descripción, cantidad y precio válidos.");
        }

        Descripcion = descripcion.Trim();
        Cantidad = cantidad;
        PrecioUnitario = precioUnitario;
        Exento = exento;
        Importe = decimal.Round(cantidad * precioUnitario, 2, MidpointRounding.AwayFromZero);
        if (Importe > 9_999_999_999_999_999.99m)
        {
            throw new BillingRuleException("El importe excede la precisión autorizada.");
        }
    }

    public int Id { get; private set; }
    public string Descripcion { get; private set; } = "";
    public decimal Cantidad { get; private set; }
    public decimal PrecioUnitario { get; private set; }
    public bool Exento { get; private set; }
    public decimal Importe { get; private set; }
}

public sealed class Factura
{
    private readonly List<LineaFactura> _lineas = [];

    private Factura() { }

    public Factura(Guid id, Guid obligadoId, Guid clienteId, Guid creadaPor,
        IReadOnlyCollection<LineaFactura> lineas)
    {
        if (id == Guid.Empty || obligadoId == Guid.Empty || clienteId == Guid.Empty || creadaPor == Guid.Empty ||
            lineas is null || lineas.Count == 0)
        {
            throw new BillingRuleException("La factura necesita identificadores y al menos una línea.");
        }

        Id = id;
        ObligadoId = obligadoId;
        ClienteId = clienteId;
        CreadaPor = creadaPor;
        _lineas = lineas.ToList();
        Subtotal = _lineas.Sum(x => x.Importe);
        Isv = decimal.Round(_lineas.Where(x => !x.Exento).Sum(x => x.Importe) * 0.15m,
            2, MidpointRounding.AwayFromZero);
        Total = Subtotal + Isv;
        if (Total > 9_999_999_999_999_999.99m)
        {
            throw new BillingRuleException("El total excede la precisión autorizada.");
        }
    }

    public Guid Id { get; private set; }
    public Guid ObligadoId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid CreadaPor { get; private set; }
    public IReadOnlyCollection<LineaFactura> Lineas => _lineas.AsReadOnly();
    public EstadoFactura Estado { get; private set; } = EstadoFactura.Borrador;
    public string? Numero { get; private set; }
    public string? Cai { get; private set; }
    public DateOnly? FechaEmision { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Isv { get; private set; }
    public decimal Total { get; private set; }

    public void Emitir(ObligadoTributario obligado, DateOnly hoy)
    {
        if (Estado != EstadoFactura.Borrador || obligado.Id != ObligadoId)
        {
            throw new BillingRuleException("Solo se puede emitir un borrador de su propio obligado.");
        }

        Numero = obligado.ReservarNumero(hoy);
        Cai = obligado.Cai;
        FechaEmision = hoy;
        Estado = EstadoFactura.Emitida;
    }

    public void Anular()
    {
        if (Estado != EstadoFactura.Emitida)
        {
            throw new BillingRuleException("Solo se puede anular una factura emitida.");
        }

        Estado = EstadoFactura.Anulada;
    }
}
