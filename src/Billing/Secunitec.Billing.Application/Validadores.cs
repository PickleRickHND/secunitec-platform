using FluentValidation;
using FluentValidation.Results;
using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Application;

// T-03 / A03-A04: validación de entrada con FluentValidation antes de tocar el dominio. El dominio repite sus
// invariantes (defensa en profundidad); estos validadores dan errores por campo para el formulario del Front-End.

public sealed class NuevaLineaValidator : AbstractValidator<NuevaLinea>
{
    public NuevaLineaValidator()
    {
        RuleFor(x => x.Descripcion).NotEmpty().WithMessage("Escriba la descripción.")
            .MaximumLength(LineaFactura.MaxDescripcion).WithMessage("La descripción admite hasta 250 caracteres.");
        RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero.")
            .LessThanOrEqualTo(999_999_999m).WithMessage("La cantidad es demasiado grande.")
            .PrecisionScale(13, 4, ignoreTrailingZeros: true).WithMessage("La cantidad admite hasta 4 decimales.");
        RuleFor(x => x.PrecioUnitario).GreaterThan(0).WithMessage("El precio debe ser mayor que cero.")
            .LessThanOrEqualTo(999_999_999m).WithMessage("El precio es demasiado grande.")
            .PrecisionScale(11, 2, ignoreTrailingZeros: true).WithMessage("El precio admite hasta 2 decimales.");
    }
}

public sealed class NuevaFacturaValidator : AbstractValidator<NuevaFactura>
{
    public NuevaFacturaValidator()
    {
        RuleFor(x => x.ClienteId).NotEmpty().WithMessage("Elija el cliente.");
        RuleFor(x => x.Lineas).NotNull().WithMessage("Agregue al menos una línea.")
            .Must(x => x is { Count: > 0 and <= Factura.MaxLineas }).WithMessage("La factura debe tener entre 1 y 100 líneas.");
        RuleForEach(x => x.Lineas).SetValidator(new NuevaLineaValidator());
    }
}

public sealed class AnularFacturaValidator : AbstractValidator<AnularFactura>
{
    public AnularFacturaValidator()
    {
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Indique el motivo de la anulación.")
            .MaximumLength(Factura.MaxMotivo).WithMessage("El motivo admite hasta 250 caracteres.");
    }
}

public sealed class DatosClienteValidator : AbstractValidator<DatosCliente>
{
    public DatosClienteValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Escriba el nombre del cliente.")
            .MaximumLength(Cliente.MaxTexto).WithMessage("El nombre admite hasta 250 caracteres.");
        RuleFor(x => x.Rtn).Matches("^[0-9]{14}$").WithMessage("El RTN debe tener 14 dígitos.")
            .When(x => !string.IsNullOrEmpty(x.Rtn));
        RuleFor(x => x.Email).EmailAddress().WithMessage("El correo no es válido.")
            .MaximumLength(Cliente.MaxTexto).WithMessage("El correo admite hasta 250 caracteres.")
            .When(x => !string.IsNullOrEmpty(x.Email));
    }
}

public sealed class ActualizarCaiValidator : AbstractValidator<ActualizarCai>
{
    public ActualizarCaiValidator()
    {
        RuleFor(x => x.Cai).Cai();
        RuleFor(x => x.Prefijo).Prefijo();
        RuleFor(x => x.RangoDesde).GreaterThanOrEqualTo(1).WithMessage("El rango empieza en 1 o más.");
        RuleFor(x => x.RangoHasta).RangoHasta(x => x.RangoDesde);
    }
}

public sealed class NuevoObligadoValidator : AbstractValidator<NuevoObligado>
{
    public NuevoObligadoValidator()
    {
        RuleFor(x => x.Rtn).NotEmpty().Matches("^[0-9]{14}$").WithMessage("El RTN debe tener 14 dígitos.");
        RuleFor(x => x.RazonSocial).NotEmpty().WithMessage("Escriba la razón social.")
            .MaximumLength(ObligadoTributario.MaxRazonSocial).WithMessage("La razón social admite hasta 250 caracteres.");
        RuleFor(x => x.Cai).Cai();
        RuleFor(x => x.Prefijo).Prefijo();
        RuleFor(x => x.RangoDesde).GreaterThanOrEqualTo(1).WithMessage("El rango empieza en 1 o más.");
        RuleFor(x => x.RangoHasta).RangoHasta(x => x.RangoDesde);
    }
}

internal static class ReglasTributarias
{
    public static IRuleBuilderOptions<T, string> Cai<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Escriba el CAI.")
            .Matches("^[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{2}$")
            .WithMessage("El CAI tiene el formato XXXXXX-XXXXXX-XXXXXX-XXXXXX-XXXXXX-XX.");

    public static IRuleBuilderOptions<T, string> Prefijo<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Escriba el prefijo.")
            .Matches("^[0-9]{3}-[0-9]{3}-[0-9]{2}$").WithMessage("El prefijo tiene el formato 000-001-01.");

    public static IRuleBuilderOptions<T, int> RangoHasta<T>(this IRuleBuilder<T, int> rule, System.Linq.Expressions.Expression<Func<T, int>> desde) =>
        rule.GreaterThanOrEqualTo(desde).WithMessage("El final del rango no puede ser menor que el inicio.")
            .LessThanOrEqualTo(ObligadoTributario.MaxCorrelativo).WithMessage("El rango admite hasta 99999999.");
}

public sealed class FiltroFacturasValidator : AbstractValidator<FiltroFacturas>
{
    public FiltroFacturasValidator()
    {
        RuleFor(x => x.Pagina).GreaterThanOrEqualTo(1).WithMessage("La página empieza en 1.");
        RuleFor(x => x.Tamano).InclusiveBetween(1, 100).WithMessage("El tamaño de página va de 1 a 100.");
        RuleFor(x => x.Hasta).GreaterThanOrEqualTo(x => x.Desde).When(x => x.Desde is not null && x.Hasta is not null)
            .WithMessage("La fecha final no puede ser anterior a la inicial.");
    }
}

public sealed class FiltroClientesValidator : AbstractValidator<FiltroClientes>
{
    public FiltroClientesValidator()
    {
        RuleFor(x => x.Buscar).MaximumLength(100).WithMessage("La búsqueda admite hasta 100 caracteres.");
        RuleFor(x => x.Pagina).GreaterThanOrEqualTo(1).WithMessage("La página empieza en 1.");
        RuleFor(x => x.Tamano).InclusiveBetween(1, 100).WithMessage("El tamaño de página va de 1 a 100.");
    }
}

public sealed class FiltroAuditoriaValidator : AbstractValidator<FiltroAuditoria>
{
    public FiltroAuditoriaValidator()
    {
        RuleFor(x => x.Accion).MaximumLength(64).Matches("^[a-z_.]*$").WithMessage("La acción no es válida.");
        RuleFor(x => x.Pagina).GreaterThanOrEqualTo(1).WithMessage("La página empieza en 1.");
        RuleFor(x => x.Tamano).InclusiveBetween(1, 200).WithMessage("El tamaño de página va de 1 a 200.");
        RuleFor(x => x.Hasta).GreaterThanOrEqualTo(x => x.Desde).When(x => x.Desde is not null && x.Hasta is not null)
            .WithMessage("La fecha final no puede ser anterior a la inicial.");
    }
}

internal static class Validacion
{
    public static readonly NuevaFacturaValidator NuevaFactura = new();
    public static readonly NuevaLineaValidator NuevaLinea = new();
    public static readonly AnularFacturaValidator AnularFactura = new();
    public static readonly DatosClienteValidator DatosCliente = new();
    public static readonly NuevoObligadoValidator NuevoObligado = new();
    public static readonly ActualizarCaiValidator ActualizarCai = new();
    public static readonly FiltroFacturasValidator FiltroFacturas = new();
    public static readonly FiltroClientesValidator FiltroClientes = new();
    public static readonly FiltroAuditoriaValidator FiltroAuditoria = new();

    public static void Exigir<T>(IValidator<T> validator, T? instance)
    {
        if (instance is null)
        {
            throw new BillingValidationException(new Dictionary<string, string[]> { [""] = ["Falta el cuerpo de la petición."] });
        }

        ValidationResult result = validator.Validate(instance);
        if (!result.IsValid)
        {
            throw new BillingValidationException(result.Errors
                .GroupBy(error => ToCamelCase(error.PropertyName))
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).Distinct().ToArray()));
        }
    }

    // Los nombres de campo del JSON (camelCase), para que el Front-End marque el control correcto.
    private static string ToCamelCase(string path) => string.Join('.', path.Split('.').Select(part =>
        part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
