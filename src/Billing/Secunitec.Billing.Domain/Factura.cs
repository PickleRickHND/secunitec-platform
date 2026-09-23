namespace Secunitec.Billing.Domain;

public enum EstadoFactura { Borrador, Emitida, Anulada }

public sealed class LineaFactura
{
    public const int MaxDescripcion = 250;
    private const decimal MaxCantidadOPrecio = 999_999_999m;

    private LineaFactura() { }

    public LineaFactura(string descripcion, decimal cantidad, decimal precioUnitario, bool exento)
    {
        // Cantidad con hasta 4 decimales y precio con hasta 2: lo que guardan numeric(18,4) y numeric(18,2). Un
        // valor con más precisión se rechaza en vez de redondearse en silencio.
        if (string.IsNullOrWhiteSpace(descripcion) || descripcion.Trim().Length > MaxDescripcion ||
            cantidad <= 0 || cantidad > MaxCantidadOPrecio || decimal.Round(cantidad, 4) != cantidad ||
            precioUnitario <= 0 || precioUnitario > MaxCantidadOPrecio || decimal.Round(precioUnitario, 2) != precioUnitario)
        {
            throw new BillingRuleException(BillingErrorCodes.LineaInvalida,
                "La línea debe tener descripción, cantidad y precio unitario positivos.");
        }

        Descripcion = descripcion.Trim();
        Cantidad = cantidad;
        PrecioUnitario = Money.Of(precioUnitario);
        Exento = exento;
        Importe = PrecioUnitario.Por(cantidad);
    }

    public int Id { get; private set; }
    public string Descripcion { get; private set; } = "";
    public decimal Cantidad { get; private set; }
    public Money PrecioUnitario { get; private set; }
    public bool Exento { get; private set; }
    public Money Importe { get; private set; }
}

/// <summary>
/// Agregado raíz. Los totales se calculan aquí y nunca se reciben del cliente (T-03, A04). Estados:
/// Borrador → Emitida → Anulada; solo un borrador admite cambios en sus líneas.
/// </summary>
public sealed class Factura
{
    public const int MaxLineas = 100;
    public const int MaxMotivo = 250;

    /// <summary>ISV de Honduras sobre las líneas no exentas.</summary>
    public const decimal TasaIsv = 0.15m;

    private readonly List<LineaFactura> _lineas = [];

    private Factura() { }

    public Factura(Guid id, Guid obligadoId, Guid clienteId, Guid creadaPor,
        IReadOnlyCollection<LineaFactura> lineas, DateTimeOffset creadaEn)
    {
        if (id == Guid.Empty || obligadoId == Guid.Empty || clienteId == Guid.Empty || creadaPor == Guid.Empty)
        {
            throw new BillingRuleException(BillingErrorCodes.FacturaInvalida, "La factura necesita obligado, cliente y autor.");
        }

        if (lineas is null || lineas.Count == 0)
        {
            throw new BillingRuleException(BillingErrorCodes.FacturaSinLineas, "La factura debe tener al menos una línea.");
        }

        if (lineas.Count > MaxLineas)
        {
            throw new BillingRuleException(BillingErrorCodes.FacturaDemasiadasLineas, $"La factura admite hasta {MaxLineas} líneas.");
        }

        Id = id;
        ObligadoId = obligadoId;
        ClienteId = clienteId;
        CreadaPor = creadaPor;
        CreadaEn = creadaEn;
        _lineas.AddRange(lineas);
        Recalcular();
    }

    public Guid Id { get; private set; }
    public Guid ObligadoId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid CreadaPor { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }
    public IReadOnlyCollection<LineaFactura> Lineas => _lineas.AsReadOnly();
    public EstadoFactura Estado { get; private set; } = EstadoFactura.Borrador;
    public string? Numero { get; private set; }
    public Cai? Cai { get; private set; }
    public DateOnly? FechaEmision { get; private set; }
    public string? MotivoAnulacion { get; private set; }
    public Money Subtotal { get; private set; }
    public Money Isv { get; private set; }
    public Money Total { get; private set; }

    public void AgregarLinea(LineaFactura linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        ExigirBorrador("agregar líneas");
        if (_lineas.Count >= MaxLineas)
        {
            throw new BillingRuleException(BillingErrorCodes.FacturaDemasiadasLineas, $"La factura admite hasta {MaxLineas} líneas.");
        }

        _lineas.Add(linea);
        Recalcular();
    }

    public void QuitarLinea(int lineaId)
    {
        ExigirBorrador("quitar líneas");
        LineaFactura linea = _lineas.FirstOrDefault(x => x.Id == lineaId)
            ?? throw new BillingRuleException(BillingErrorCodes.LineaNoEncontrada, "La línea no existe en esta factura.");
        if (_lineas.Count == 1)
        {
            throw new BillingRuleException(BillingErrorCodes.FacturaUltimaLinea, "La factura debe conservar al menos una línea.");
        }

        _lineas.Remove(linea);
        Recalcular();
    }

    public void Emitir(ObligadoTributario obligado, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(obligado);
        if (Estado != EstadoFactura.Borrador || obligado.Id != ObligadoId)
        {
            throw new BillingRuleException(BillingErrorCodes.FacturaEstadoInvalido, "Solo se puede emitir un borrador de su propio obligado.");
        }

        Numero = obligado.ReservarNumero(hoy);
        Cai = obligado.Cai;
        FechaEmision = hoy;
        Estado = EstadoFactura.Emitida;
    }

    public void Anular(string motivo)
    {
        if (Estado != EstadoFactura.Emitida)
        {
            throw new BillingRuleException(BillingErrorCodes.FacturaEstadoInvalido, "Solo se puede anular una factura emitida.");
        }

        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > MaxMotivo)
        {
            throw new BillingRuleException(BillingErrorCodes.MotivoRequerido, $"Indique el motivo de la anulación (hasta {MaxMotivo} caracteres).");
        }

        MotivoAnulacion = motivo.Trim();
        Estado = EstadoFactura.Anulada;
    }

    private void ExigirBorrador(string accion)
    {
        if (Estado != EstadoFactura.Borrador)
        {
            throw new BillingRuleException(BillingErrorCodes.FacturaEstadoInvalido, $"Solo se pueden {accion} en un borrador.");
        }
    }

    private void Recalcular()
    {
        Subtotal = _lineas.Aggregate(Money.Zero, (total, linea) => total + linea.Importe);
        Isv = _lineas.Where(x => !x.Exento).Aggregate(Money.Zero, (total, linea) => total + linea.Importe).Por(TasaIsv);
        Total = Subtotal + Isv;
    }
}
