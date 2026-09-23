#!/usr/bin/env python3
"""Genera los tres planes de JMeter de la etapa 5.3 (R16) con la misma estructura.

    python3 tests/jmeter/generar-planes.py

Cada plan tiene:
- Un setUp thread group que pide un token por client credentials para cada cliente de carga (jmeter-load-01..NN).
  Espera el Retry-After si /connect/token (5 por minuto por IP) lo limita. El secreto se lee de la variable de entorno
  JMETER_CLIENT_SECRET (scripts/stress/run-jmeter.sh), nunca de la línea de comandos: JMeter registra las -J en su log.
- Un thread group de carga. Cada hilo usa el token del cliente (número de hilo % clientes) + 1, así los hilos se reparten
  entre las particiones de 60 por minuto del gateway sin bajar los límites.
- Una mezcla de peticiones: 60 % lista (página y estado al azar, para no servir todo desde la caché de 30 s), 20 %
  detalle por id, 17 % alta de borradores y 3 % emisiones.
- Una aserción que acepta 200, 201 y 429: en el reporte, "error" es un 401, un 403, un 5xx o una falla de conexión.

Los .jmx se versionan; editar este generador y volver a correrlo en lugar de editarlos a mano.
"""
from pathlib import Path
from xml.sax.saxutils import escape

AQUI = Path(__file__).resolve().parent

PLANES = {
    "01-baseline": {
        "descripcion": "Línea base: 50 usuarios por debajo del límite (480 peticiones/min contra 600/min de 10 clientes). "
                       "Se espera 0 % de 429 y latencia estable.",
        "hilos": 50, "rampa": 30, "duracion": 300, "ritmo_por_minuto": 480, "pausa": None,
    },
    "02-ramp-saturation": {
        "descripcion": "Rampa a saturación: de 0 a 500 usuarios en 8 minutos y 2 minutos sostenidos, con pausas de "
                       "100 a 300 ms. El gateway pasa de aceptar todo a bloquear con 429; meta: cero 5xx (R18).",
        "hilos": 500, "rampa": 480, "duracion": 600, "ritmo_por_minuto": None, "pausa": (100, 200),
    },
    "03-spike": {
        "descripcion": "Pico: 1000 usuarios en 10 segundos, sostenidos 3 minutos, con pausas de 100 a 300 ms. "
                       "Meta: 429 controlados y cero 5xx (R18).",
        "hilos": 1000, "rampa": 10, "duracion": 190, "ritmo_por_minuto": None, "pausa": (100, 200),
    },
}

TOKENS = r'''
// setUp (etapa 5.3): un token por cliente de carga. Valida el certificado del gateway (infra/certs/gateway.pem):
// los muestreadores HTTP de JMeter no lo validan, pero el secreto solo viaja por una conexión verificada.
import groovy.json.JsonSlurper
import java.net.http.HttpClient
import java.net.http.HttpRequest
import java.net.http.HttpResponse
import java.security.KeyStore
import java.security.cert.CertificateFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManagerFactory

String secreto = System.getenv('JMETER_CLIENT_SECRET')
if (!secreto) {
    SampleResult.setSuccessful(false)
    SampleResult.setResponseMessage('Falta JMETER_CLIENT_SECRET (usar scripts/stress/run-jmeter.sh)')
    return
}
int clientes = (props.get('clientes') ?: '10') as int
String base = "https://${props.get('host') ?: 'localhost'}:${props.get('port') ?: '8080'}"
def certificado = new File(props.get('ca')).withInputStream { CertificateFactory.getInstance('X.509').generateCertificate(it) }
KeyStore almacen = KeyStore.getInstance(KeyStore.getDefaultType())
almacen.load(null, null)
almacen.setCertificateEntry('gateway', certificado)
TrustManagerFactory confianza = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm())
confianza.init(almacen)
SSLContext tls = SSLContext.getInstance('TLS')
tls.init(null, confianza.getTrustManagers(), null)
HttpClient http = HttpClient.newBuilder().sslContext(tls).build()

for (int i = 1; i <= clientes; i++) {
    String cliente = String.format('jmeter-load-%02d', i)
    String cuerpo = 'grant_type=client_credentials&scope=secunitec-billing&client_id=' + cliente +
        '&client_secret=' + URLEncoder.encode(secreto, 'UTF-8')
    while (true) {
        HttpResponse<String> respuesta = http.send(HttpRequest.newBuilder(URI.create(base + '/connect/token'))
            .header('Content-Type', 'application/x-www-form-urlencoded')
            .POST(HttpRequest.BodyPublishers.ofString(cuerpo)).build(), HttpResponse.BodyHandlers.ofString())
        if (respuesta.statusCode() == 200) {
            props.put('token_' + i, new JsonSlurper().parseText(respuesta.body()).access_token)
            break
        }
        if (respuesta.statusCode() == 429) {
            int espera = (respuesta.headers().firstValue('Retry-After').orElse('60')) as int
            log.info("/connect/token limitado: ${cliente} espera ${espera + 1} s (Retry-After)")
            Thread.sleep((espera + 1) * 1000L)
            continue
        }
        SampleResult.setSuccessful(false)
        SampleResult.setResponseMessage("Token de ${cliente}: HTTP ${respuesta.statusCode()}")
        return
    }
}
props.put('clientes', clientes as String)
SampleResult.setResponseData("${clientes} tokens listos", 'UTF-8')
'''.strip()

# Índice del Switch: 0 lista, 1 detalle, 2 alta, 3 emisión. Sin comas: separan parámetros en las funciones de JMeter.
MEZCLA = ("${__groovy(def r = Math.random(); def f = vars.get('ultima_factura'); def hay = f != null && f != 'NINGUNA'; "
          "if (r < 0.60 || (!hay && r < 0.80)) return 0; if (r < 0.80) return 1; if (r < 0.97 || !hay) return 2; return 3)}")
TOKEN_DEL_HILO = "${__groovy(props.get('token_' + (ctx.getThreadNum() % (props.get('clientes') as int) + 1)))}"
ESTADO = "${__groovy(Math.random() < 0.5 ? 'Borrador' : 'Emitida')}"


def prop(tipo, nombre, valor):
    return f'<{tipo}Prop name="{nombre}">{escape(str(valor))}</{tipo}Prop>'


def argumentos(pares):
    elementos = "".join(
        f'<elementProp name="{escape(n)}" elementType="Argument">{prop("string", "Argument.name", n)}'
        f'{prop("string", "Argument.value", v)}{prop("string", "Argument.metadata", "=")}</elementProp>'
        for n, v in pares)
    return (f'<elementProp name="TestPlan.user_defined_variables" elementType="Arguments" guiclass="ArgumentsPanel" '
            f'testclass="Arguments" testname="Variables"><collectionProp name="Arguments.arguments">{elementos}'
            f'</collectionProp></elementProp>')


def muestreador(nombre, metodo, ruta, cuerpo=None, hijos=""):
    if cuerpo is None:
        args = ('<elementProp name="HTTPsampler.Arguments" elementType="Arguments" guiclass="HTTPArgumentsPanel" '
                'testclass="Arguments"><collectionProp name="Arguments.arguments"/></elementProp>')
        raw = ""
    else:
        args = ('<elementProp name="HTTPsampler.Arguments" elementType="Arguments"><collectionProp name="Arguments.arguments">'
                f'<elementProp name="" elementType="HTTPArgument">{prop("bool", "HTTPArgument.always_encode", "false")}'
                f'{prop("string", "Argument.value", cuerpo)}{prop("string", "Argument.metadata", "=")}</elementProp>'
                '</collectionProp></elementProp>')
        raw = prop("bool", "HTTPSampler.postBodyRaw", "true")
    return (f'<HTTPSamplerProxy guiclass="HttpTestSampleGui" testclass="HTTPSamplerProxy" testname="{escape(nombre)}">'
            f'{raw}{args}{prop("string", "HTTPSampler.path", ruta)}{prop("string", "HTTPSampler.method", metodo)}'
            f'{prop("bool", "HTTPSampler.follow_redirects", "false")}{prop("bool", "HTTPSampler.use_keepalive", "true")}'
            f'</HTTPSamplerProxy><hashTree>{hijos}</hashTree>')


def plan(nombre, p):
    pausa = ""
    if p["pausa"]:
        base, rango = p["pausa"]
        pausa = (f'<UniformRandomTimer guiclass="UniformRandomTimerGui" testclass="UniformRandomTimer" '
                 f'testname="Pausa de {base} a {base + rango} ms">{prop("string", "ConstantTimer.delay", base)}'
                 f'{prop("string", "RandomTimer.range", rango)}</UniformRandomTimer><hashTree/>')
    ritmo = ""
    if p["ritmo_por_minuto"]:
        # calcMode 4: todos los hilos del grupo comparten el ritmo total.
        ritmo = (f'<ConstantThroughputTimer guiclass="TestBeanGUI" testclass="ConstantThroughputTimer" '
                 f'testname="Ritmo total por minuto">{prop("int", "calcMode", 4)}'
                 f'{prop("string", "throughput", "${__P(ritmo," + str(p["ritmo_por_minuto"]) + ")}")}'
                 f'</ConstantThroughputTimer><hashTree/>')
    json_id = ('<JSONPostProcessor guiclass="JSONPostProcessorGui" testclass="JSONPostProcessor" testname="id de la factura creada">'
               f'{prop("string", "JSONPostProcessor.referenceNames", "ultima_factura")}'
               f'{prop("string", "JSONPostProcessor.jsonPathExprs", "$.id")}'
               f'{prop("string", "JSONPostProcessor.match_numbers", "1")}'
               f'{prop("string", "JSONPostProcessor.defaultValues", "NINGUNA")}</JSONPostProcessor><hashTree/>')
    tras_emitir = ('<JSR223PostProcessor guiclass="TestBeanGUI" testclass="JSR223PostProcessor" testname="Una factura se emite una sola vez">'
                   f'{prop("string", "scriptLanguage", "groovy")}{prop("string", "cacheKey", "true")}'
                   f'{prop("string", "script", "if (prev.getResponseCode() == \"200\") { vars.put(\"ultima_factura\", \"NINGUNA\") }")}'
                   '</JSR223PostProcessor><hashTree/>')
    alta = '{"clienteId":"${cliente_id}","lineas":[{"descripcion":"Carga JMeter","cantidad":1,"precioUnitario":100,"exento":false}]}'
    mezcla = (
        muestreador("GET /api/billing/facturas", "GET",
                    "/api/billing/facturas?pagina=${__Random(1,5)}&tamano=20&estado=" + ESTADO)
        + muestreador("GET /api/billing/facturas/{id}", "GET", "/api/billing/facturas/${ultima_factura}")
        + muestreador("POST /api/billing/facturas", "POST", "/api/billing/facturas", alta, json_id)
        + muestreador("POST /api/billing/facturas/{id}/emitir", "POST", "/api/billing/facturas/${ultima_factura}/emitir", "", tras_emitir)
    )
    cabeceras = [
        ("Authorization", "Bearer " + TOKEN_DEL_HILO),
        ("Content-Type", "application/json"),
        ("X-Correlation-Id", "jmeter-" + nombre + "-${__threadNum}-${__time()}"),
    ]
    header_manager = ('<HeaderManager guiclass="HeaderPanel" testclass="HeaderManager" testname="Token del cliente del hilo y correlation id">'
                      '<collectionProp name="HeaderManager.headers">'
                      + "".join(f'<elementProp name="" elementType="Header">{prop("string", "Header.name", n)}{prop("string", "Header.value", v)}</elementProp>'
                                for n, v in cabeceras)
                      + '</collectionProp></HeaderManager><hashTree/>')
    asercion = ('<ResponseAssertion guiclass="AssertionGui" testclass="ResponseAssertion" testname="Esperado: 200, 201 o 429">'
                '<collectionProp name="Asserion.test_strings"><stringProp name="0">200|201|429</stringProp></collectionProp>'
                f'{prop("string", "Assertion.custom_message", "Ni 401, ni 403, ni 5xx, ni fallas de conexión")}'
                f'{prop("string", "Assertion.test_field", "Assertion.response_code")}'
                f'{prop("bool", "Assertion.assume_success", "true")}{prop("int", "Assertion.test_type", 1)}'
                '</ResponseAssertion><hashTree/>')
    defaults = ('<ConfigTestElement guiclass="HttpDefaultsGui" testclass="ConfigTestElement" testname="Gateway (HTTPS)">'
                '<elementProp name="HTTPsampler.Arguments" elementType="Arguments" guiclass="HTTPArgumentsPanel" testclass="Arguments">'
                '<collectionProp name="Arguments.arguments"/></elementProp>'
                f'{prop("string", "HTTPSampler.domain", "${host}")}{prop("string", "HTTPSampler.port", "${port}")}'
                f'{prop("string", "HTTPSampler.protocol", "https")}{prop("string", "HTTPSampler.implementation", "HttpClient4")}'
                f'{prop("string", "HTTPSampler.connect_timeout", "5000")}{prop("string", "HTTPSampler.response_timeout", "30000")}'
                '</ConfigTestElement><hashTree/>')
    setup = ('<SetupThreadGroup guiclass="SetupThreadGroupGui" testclass="SetupThreadGroup" testname="setUp: tokens por client credentials">'
             f'{prop("string", "ThreadGroup.on_sample_error", "stoptest")}'
             '<elementProp name="ThreadGroup.main_controller" elementType="LoopController" guiclass="LoopControlPanel" testclass="LoopController">'
             f'{prop("string", "LoopController.loops", "1")}{prop("bool", "LoopController.continue_forever", "false")}</elementProp>'
             f'{prop("string", "ThreadGroup.num_threads", "1")}{prop("string", "ThreadGroup.ramp_time", "1")}'
             '</SetupThreadGroup><hashTree>'
             '<JSR223Sampler guiclass="TestBeanGUI" testclass="JSR223Sampler" testname="setUp: tokens">'
             f'{prop("string", "scriptLanguage", "groovy")}{prop("string", "cacheKey", "true")}{prop("string", "script", TOKENS)}'
             '</JSR223Sampler><hashTree/></hashTree>')
    carga = ('<ThreadGroup guiclass="ThreadGroupGui" testclass="ThreadGroup" testname="Carga">'
             f'{prop("string", "ThreadGroup.on_sample_error", "continue")}'
             '<elementProp name="ThreadGroup.main_controller" elementType="LoopController" guiclass="LoopControlPanel" testclass="LoopController">'
             f'{prop("int", "LoopController.loops", -1)}{prop("bool", "LoopController.continue_forever", "false")}</elementProp>'
             f'{prop("string", "ThreadGroup.num_threads", "${__P(hilos," + str(p["hilos"]) + ")}")}'
             f'{prop("string", "ThreadGroup.ramp_time", "${__P(rampa," + str(p["rampa"]) + ")}")}'
             f'{prop("bool", "ThreadGroup.scheduler", "true")}'
             f'{prop("string", "ThreadGroup.duration", "${__P(duracion," + str(p["duracion"]) + ")}")}'
             f'{prop("string", "ThreadGroup.delay", "0")}{prop("bool", "ThreadGroup.same_user_on_next_iteration", "true")}'
             '</ThreadGroup><hashTree>'
             + header_manager + asercion + ritmo + pausa
             + '<SwitchController guiclass="SwitchControllerGui" testclass="SwitchController" testname="Mezcla: lista, detalle, alta y emisión">'
             + prop("string", "SwitchController.value", MEZCLA) + '</SwitchController><hashTree>' + mezcla + '</hashTree>'
             + '</hashTree>')
    variables = argumentos([
        ("host", "${__P(host,localhost)}"),
        ("port", "${__P(port,8080)}"),
        ("cliente_id", "${__P(cliente_id,00000000-0000-0000-0000-0000000000c2)}"),
    ])
    return ('<?xml version="1.0" encoding="UTF-8"?>\n'
            '<jmeterTestPlan version="1.2" properties="5.0" jmeter="5.6.3"><hashTree>'
            f'<TestPlan guiclass="TestPlanGui" testclass="TestPlan" testname="Secunitec {nombre}">'
            f'{prop("string", "TestPlan.comments", p["descripcion"])}'
            f'{prop("bool", "TestPlan.functional_mode", "false")}{prop("bool", "TestPlan.serialize_threadgroups", "false")}'
            f'{variables}</TestPlan><hashTree>{defaults}{setup}{carga}</hashTree></hashTree></jmeterTestPlan>\n')


for nombre, parametros in PLANES.items():
    (AQUI / f"{nombre}.jmx").write_text(plan(nombre, parametros), encoding="utf-8")
    print(f"tests/jmeter/{nombre}.jmx")
