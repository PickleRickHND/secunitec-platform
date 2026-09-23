using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Api.Tests;

// Criterio de done de 2.2 (README d688497): crear → emitir → anular por HTTP; correlativo consecutivo bajo
// concurrencia; usuario de otro tenant recibe 403; el evento de auditoría queda escrito.
public sealed class BillingApiTests : IClassFixture<BillingFactory>
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static int _rtn;

    private readonly BillingFactory _factory;

    public BillingApiTests(BillingFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Flujo_CrearEmitirAnular_PorHttp_ConAuditoriaCompleta()
    {
        SkipWithoutDocker();
        (Guid tenant, Guid clienteId) = await NuevoTenantAsync();
        Guid actor = Guid.NewGuid();
        using HttpClient client = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Facturador, actor), forwardedFor: "203.0.113.9");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "flujo-" + tenant.ToString("N")[..8]);

        using HttpResponseMessage creada = await client.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteId), Ct);
        JsonElement borrador = await Json(creada);
        string id = borrador.GetProperty("id").GetString()!;
        JsonElement emitida = await Json(await client.PostAsync($"/api/billing/facturas/{id}/emitir", null, Ct));
        JsonElement anulada = await Json(await client.PostAsJsonAsync($"/api/billing/facturas/{id}/anular", new { motivo = "Error en el RTN del cliente" }, Ct));

        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        Assert.Equal("Borrador", borrador.GetProperty("estado").GetString());
        Assert.Equal(165.00m, borrador.GetProperty("total").GetDecimal());
        Assert.Equal("Cliente de prueba", borrador.GetProperty("clienteNombre").GetString());
        Assert.Equal("000-001-01-00000001", emitida.GetProperty("numero").GetString());
        Assert.Equal("Anulada", anulada.GetProperty("estado").GetString());
        Assert.Equal("Error en el RTN del cliente", anulada.GetProperty("motivoAnulacion").GetString());

        List<BsonDocument> eventos = await _factory.AuditDb.GetCollection<BsonDocument>("eventos")
            .Find(Builders<BsonDocument>.Filter.Eq("recurso", id)).SortBy(x => x["timestamp"]).ToListAsync(Ct);
        Assert.Equal(["factura.creada", "factura.emitida", "factura.anulada"], eventos.Select(x => x["accion"].AsString));
        Assert.All(eventos, evento =>
        {
            Assert.Equal("exito", evento["resultado"].AsString);
            Assert.Equal(actor.ToString("D"), evento["actor"].AsString);
            Assert.Equal("203.0.113.9", evento["ip"].AsString);
            Assert.StartsWith("flujo-", evento["correlationId"].AsString, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Correlativo_VeinteEmisionesConcurrentes_SinHuecosNiRepetidos()
    {
        SkipWithoutDocker();
        (Guid tenant, Guid clienteId) = await NuevoTenantAsync();
        string token = BillingFactory.Token(tenant, SecunitecRoles.Facturador);
        using HttpClient client = _factory.Client(token);
        List<string> ids = [];
        for (int i = 0; i < 20; i++)
        {
            ids.Add((await Json(await client.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteId), Ct))).GetProperty("id").GetString()!);
        }

        string[] numeros = await Task.WhenAll(ids.Select(async id =>
        {
            using HttpClient paralelo = _factory.Client(token);
            using HttpResponseMessage response = await paralelo.PostAsync($"/api/billing/facturas/{id}/emitir", null, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Json(response)).GetProperty("numero").GetString()!;
        }));

        Assert.Equal(Enumerable.Range(1, 20).Select(n => $"000-001-01-{n:D8}"), numeros.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OtroTenant_RecibeForbidden_YQuedaAuditado()
    {
        SkipWithoutDocker();
        (Guid tenantA, Guid clienteA) = await NuevoTenantAsync();
        (Guid tenantB, _) = await NuevoTenantAsync();
        using HttpClient deA = _factory.Client(BillingFactory.Token(tenantA, SecunitecRoles.Facturador));
        string id = (await Json(await deA.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteA), Ct))).GetProperty("id").GetString()!;
        using HttpClient deB = _factory.Client(BillingFactory.Token(tenantB, SecunitecRoles.Admin));

        using HttpResponseMessage leer = await deB.GetAsync($"/api/billing/facturas/{id}", Ct);
        using HttpResponseMessage emitir = await deB.PostAsync($"/api/billing/facturas/{id}/emitir", null, Ct);
        using HttpResponseMessage inexistente = await deB.GetAsync($"/api/billing/facturas/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, leer.StatusCode);
        Assert.Equal("acceso.denegado", (await Json(leer)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, emitir.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        long denegados = await _factory.AuditDb.GetCollection<BsonDocument>("eventos").CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Eq("tenant_id", tenantB.ToString("D")) &
            Builders<BsonDocument>.Filter.Eq("accion", "acceso.denegado") &
            Builders<BsonDocument>.Filter.Eq("recurso", id), cancellationToken: Ct);
        Assert.Equal(2, denegados);
        // La factura de A no cambió.
        Assert.Equal("Borrador", (await Json(await deA.GetAsync($"/api/billing/facturas/{id}", Ct))).GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Auditor_Emitir_ForbiddenPorPolitica_YQuedaAuditado()
    {
        SkipWithoutDocker();
        (Guid tenant, Guid clienteId) = await NuevoTenantAsync();
        using HttpClient facturador = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Facturador));
        string id = (await Json(await facturador.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteId), Ct))).GetProperty("id").GetString()!;
        using HttpClient auditor = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Auditor));

        using HttpResponseMessage response = await auditor.PostAsync($"/api/billing/facturas/{id}/emitir", null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        BsonDocument evento = await _factory.AuditDb.GetCollection<BsonDocument>("eventos").Find(
            Builders<BsonDocument>.Filter.Eq("tenant_id", tenant.ToString("D")) &
            Builders<BsonDocument>.Filter.Eq("accion", "acceso.denegado")).SingleAsync(Ct);
        Assert.Equal("denegado", evento["resultado"].AsString);
        Assert.Equal($"POST /api/billing/facturas/{id}/emitir", evento["recurso"].AsString);
    }

    [Fact]
    public async Task Errores_TraenCodigoEstableSinDetallesInternos()
    {
        SkipWithoutDocker();
        (Guid tenant, Guid clienteId) = await NuevoTenantAsync();
        using HttpClient client = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Admin));
        string id = (await Json(await client.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteId), Ct))).GetProperty("id").GetString()!;

        using HttpResponseMessage sinLineas = await client.PostAsJsonAsync("/api/billing/facturas", new { clienteId, lineas = Array.Empty<object>() }, Ct);
        using HttpResponseMessage anularBorrador = await client.PostAsJsonAsync($"/api/billing/facturas/{id}/anular", new { motivo = "x" }, Ct);
        using HttpResponseMessage caiVencido = await client.PutAsJsonAsync("/api/billing/obligados/actual/cai", new
        {
            cai = "ZZZZZZ-YYYYYY-XXXXXX-WWWWWW-VVVVVV-UU",
            prefijo = "000-001-01",
            rangoDesde = 1,
            rangoHasta = 100,
            fechaLimiteEmision = "2020-01-31",
        }, Ct);
        using HttpResponseMessage emitirVencido = await client.PostAsync($"/api/billing/facturas/{id}/emitir", null, Ct);

        JsonElement validacion = await Json(sinLineas);
        Assert.Equal(HttpStatusCode.BadRequest, sinLineas.StatusCode);
        Assert.Equal("validacion", validacion.GetProperty("code").GetString());
        Assert.True(validacion.GetProperty("errors").TryGetProperty("lineas", out _));
        Assert.Equal("factura.estado_invalido", (await Json(anularBorrador)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, caiVencido.StatusCode);
        string cuerpo = await emitirVencido.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.BadRequest, emitirVencido.StatusCode);
        Assert.Contains("\"code\":\"cai.vencido\"", cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Listado_PaginaYFiltraPorEstado()
    {
        SkipWithoutDocker();
        (Guid tenant, Guid clienteId) = await NuevoTenantAsync();
        using HttpClient client = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Facturador));
        string primera = (await Json(await client.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteId), Ct))).GetProperty("id").GetString()!;
        await client.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteId), Ct);
        await client.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteId), Ct);
        await client.PostAsync($"/api/billing/facturas/{primera}/emitir", null, Ct);

        JsonElement pagina = await Json(await client.GetAsync("/api/billing/facturas?tamano=2", Ct));
        JsonElement emitidas = await Json(await client.GetAsync("/api/billing/facturas?estado=Emitida", Ct));
        using HttpResponseMessage invalido = await client.GetAsync("/api/billing/facturas?tamano=1000", Ct);

        Assert.Equal(3, pagina.GetProperty("total").GetInt32());
        Assert.Equal(2, pagina.GetProperty("items").GetArrayLength());
        Assert.Equal(1, emitidas.GetProperty("total").GetInt32());
        Assert.Equal(primera, emitidas.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    [Fact]
    public async Task Lineas_AgregarYQuitar_RecalculaEnElServidor()
    {
        SkipWithoutDocker();
        (Guid tenant, Guid clienteId) = await NuevoTenantAsync();
        using HttpClient client = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Facturador));
        string id = (await Json(await client.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteId), Ct))).GetProperty("id").GetString()!;

        JsonElement conTres = await Json(await client.PostAsJsonAsync($"/api/billing/facturas/{id}/lineas",
            new { descripcion = "Visita técnica", cantidad = 2, precioUnitario = 25.50m, exento = false }, Ct));
        int lineaId = conTres.GetProperty("lineas")[2].GetProperty("id").GetInt32();
        JsonElement sinLaTercera = await Json(await client.DeleteAsync($"/api/billing/facturas/{id}/lineas/{lineaId}", Ct));

        Assert.Equal(3, conTres.GetProperty("lineas").GetArrayLength());
        Assert.Equal(223.65m, conTres.GetProperty("total").GetDecimal());
        Assert.Equal(2, sinLaTercera.GetProperty("lineas").GetArrayLength());
        Assert.Equal(165.00m, sinLaTercera.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task RolCliente_SoloVeSusFacturas()
    {
        SkipWithoutDocker();
        (Guid tenant, Guid propio) = await NuevoTenantAsync();
        using HttpClient facturador = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Facturador));
        Guid ajeno = Guid.Parse((await Json(await facturador.PostAsJsonAsync("/api/billing/clientes",
            new { nombre = "Otro cliente", rtn = (string?)null, email = (string?)null }, Ct))).GetProperty("id").GetString()!);
        string suya = (await Json(await facturador.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(propio), Ct))).GetProperty("id").GetString()!;
        string otra = (await Json(await facturador.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(ajeno), Ct))).GetProperty("id").GetString()!;
        using HttpClient cliente = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Cliente, clienteId: propio));

        JsonElement listado = await Json(await cliente.GetAsync("/api/billing/facturas", Ct));
        using HttpResponseMessage ajena = await cliente.GetAsync($"/api/billing/facturas/{otra}", Ct);
        using HttpResponseMessage clientes = await cliente.GetAsync("/api/billing/clientes", Ct);

        Assert.Equal(1, listado.GetProperty("total").GetInt32());
        Assert.Equal(suya, listado.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clientes.StatusCode);
    }

    [Fact]
    public async Task Auditoria_AuditorVeEventosDeBillingYDeIdentityDeSuTenant()
    {
        SkipWithoutDocker();
        (Guid tenant, Guid clienteId) = await NuevoTenantAsync();
        using HttpClient facturador = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Facturador));
        await facturador.PostAsJsonAsync("/api/billing/facturas", NuevaFactura(clienteId), Ct);
        // Un login de este tenant y otro de un tenant ajeno, como los escribe Identity.
        IMongoCollection<BsonDocument> identity = _factory.AuditDb.GetCollection<BsonDocument>("identity_events");
        await identity.InsertManyAsync([LoginEvent(tenant), LoginEvent(Guid.NewGuid())], cancellationToken: Ct);
        using HttpClient auditor = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Auditor));

        JsonElement pagina = await Json(await auditor.GetAsync("/api/billing/auditoria", Ct));
        JsonElement soloLogins = await Json(await auditor.GetAsync("/api/billing/auditoria?accion=login", Ct));
        using HttpResponseMessage facturadorNoPuede = await facturador.GetAsync("/api/billing/auditoria", Ct);

        string[] origenes = pagina.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("origen").GetString()!).ToArray();
        Assert.Contains("billing", origenes);
        Assert.Contains("identity", origenes);
        Assert.Equal(1, soloLogins.GetProperty("total").GetInt32());
        Assert.Equal("Fallido", soloLogins.GetProperty("items")[0].GetProperty("resultado").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, facturadorNoPuede.StatusCode);
    }

    [Fact]
    public async Task MongoSetup_EsIdempotente_YBillingNoPuedeBorrarNiModificarLaAuditoria()
    {
        SkipWithoutDocker();
        await _factory.RunMongoSetupAsync();
        IMongoDatabase comoBilling = new MongoClient(_factory.MongoConnectionString("billing_audit", BillingFactory.BillingMongoPassword))
            .GetDatabase("secunitec_audit");
        IMongoCollection<BsonDocument> eventos = comoBilling.GetCollection<BsonDocument>("eventos");

        await eventos.InsertOneAsync(new BsonDocument { ["accion"] = "prueba.permisos", ["tenant_id"] = "ninguno" }, cancellationToken: Ct);
        long leidos = await eventos.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("accion", "prueba.permisos"), cancellationToken: Ct);
        MongoCommandException borrar = await Assert.ThrowsAsync<MongoCommandException>(() =>
            eventos.DeleteManyAsync(Builders<BsonDocument>.Filter.Eq("accion", "prueba.permisos"), Ct));
        MongoCommandException modificar = await Assert.ThrowsAsync<MongoCommandException>(() => eventos.UpdateManyAsync(
            Builders<BsonDocument>.Filter.Eq("accion", "prueba.permisos"), Builders<BsonDocument>.Update.Set("accion", "x"), cancellationToken: Ct));
        MongoCommandException escribirEnIdentity = await Assert.ThrowsAsync<MongoCommandException>(() =>
            comoBilling.GetCollection<BsonDocument>("identity_events").InsertOneAsync(new BsonDocument(), cancellationToken: Ct));

        Assert.Equal(1, leidos);
        Assert.Equal(13, borrar.Code);
        Assert.Equal(13, modificar.Code);
        Assert.Equal(13, escribirEnIdentity.Code);
    }

    private static BsonDocument LoginEvent(Guid tenant) => new()
    {
        ["timestamp"] = DateTime.UtcNow,
        ["action"] = "login",
        ["success"] = false,
        ["actor"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
        ["tenantId"] = new BsonBinaryData(tenant, GuidRepresentation.Standard),
        ["ip"] = "203.0.113.10",
        ["correlationId"] = "login-" + tenant.ToString("N")[..8],
        ["detail"] = "invalid_credentials",
    };

    private static object NuevaFactura(Guid clienteId) => new
    {
        clienteId,
        lineas = new object[]
        {
            new { descripcion = "Monitoreo mensual", cantidad = 1, precioUnitario = 100.00m, exento = false },
            new { descripcion = "Instalación", cantidad = 1, precioUnitario = 50.00m, exento = true },
        },
    };

    // Tenant nuevo por test: obligado con CAI vigente (Admin) y un cliente (Facturador).
    private async Task<(Guid Tenant, Guid ClienteId)> NuevoTenantAsync()
    {
        Guid tenant = Guid.NewGuid();
        string rtn = (80_000_000_000_000L + Interlocked.Increment(ref _rtn) + (tenant.GetHashCode() & 0xFFFFFF) * 1000L)
            .ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(14, '0')[..14];
        using HttpClient admin = _factory.Client(BillingFactory.Token(tenant, SecunitecRoles.Admin));
        using HttpResponseMessage obligado = await admin.PostAsJsonAsync("/api/billing/obligados", new
        {
            rtn,
            razonSocial = "Empresa de prueba",
            cai = "A1B2C3-D4E5F6-A7B8C9-D0E1F2-A3B4C5-D6",
            prefijo = "000-001-01",
            rangoDesde = 1,
            rangoHasta = 1000,
            fechaLimiteEmision = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, obligado.StatusCode);

        using HttpResponseMessage cliente = await admin.PostAsJsonAsync("/api/billing/clientes",
            new { nombre = "Cliente de prueba", rtn = (string?)null, email = "compras@cliente.hn" }, Ct);
        Assert.Equal(HttpStatusCode.Created, cliente.StatusCode);
        return (tenant, Guid.Parse((await Json(cliente)).GetProperty("id").GetString()!));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private void SkipWithoutDocker() => Assert.SkipWhen(_factory.SkipReason is not null, _factory.SkipReason ?? "");
}
