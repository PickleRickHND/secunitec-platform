import { type SubmitEvent, useMemo, useState } from 'react';
import { Roles } from '../../auth/roles';
import { useSesion } from '../../auth/useSesion';
import { KpiCard, KpiRow } from '../../components/KpiCard';
import { PageHeader } from '../../components/PageHeader';
import { config } from '../../config';
import { formatEntero, porcentaje } from '../../lib/format';
import { useCountdown } from '../../lib/useCountdown';
import {
  CLASES,
  contar,
  type Escenario,
  escenariosDisponibles,
  etiquetaClase,
  objetivoFueraDeRol,
  serieAcumulada,
  validarConfiguracion,
} from './burst';
import { claseSerie } from './series';
import { TrafficChart } from './TrafficChart';
import { useBurst } from './useBurst';
import styles from './Resiliencia.module.css';

const descripcionEscenario: Record<Escenario, { nombre: string; detalle: string }> = {
  sesion: { nombre: 'Con mi sesión', detalle: 'Su token válido contra el listado de facturas: 200 hasta el límite y luego 429.' },
  sinToken: { nombre: 'Sin token', detalle: 'Sin Authorization: 401. Se limita por IP, así que también termina en 429.' },
  tokenAlterado: { nombre: 'Token alterado', detalle: 'Su token con la firma modificada: el gateway lo rechaza con 401.' },
  fueraDeRol: { nombre: 'Fuera de mi rol', detalle: 'Un endpoint que su rol no tiene: 403. Cada intento queda en la auditoría.' },
  mixto: { nombre: 'Mixto', detalle: 'Alterna los escenarios anteriores en la misma ráfaga.' },
};

export function ResilienciaPage() {
  const { roles, accessToken } = useSesion();
  const burst = useBurst(accessToken, roles);
  const [escenario, setEscenario] = useState<Escenario>('sesion');
  const [peticiones, setPeticiones] = useState('120');
  const [segundos, setSegundos] = useState('10');
  const [tabla, setTabla] = useState(false);
  const restante = useCountdown(burst.bloqueadoHasta);
  const disponibles = escenariosDisponibles(roles);
  const error = validarConfiguracion(Number(peticiones), Number(segundos));

  const conteo = useMemo(() => contar(burst.resultados), [burst.resultados]);
  const hasta = Math.max(burst.transcurrido, burst.rafaga?.segundos ?? 0);
  const serie = useMemo(() => serieAcumulada(burst.resultados, hasta), [burst.resultados, hasta]);
  const total = burst.resultados.length;
  const recientes = burst.resultados.slice(-12).reverse();

  const lanzar = (event: SubmitEvent) => {
    event.preventDefault();
    if (!error) {
      void burst.lanzar({ escenario, peticiones: Number(peticiones), segundos: Number(segundos) });
    }
  };

  return (
    <>
      <PageHeader
        titulo="Resiliencia del gateway"
        descripcion="Lance una ráfaga de peticiones reales y vea cómo el gateway responde 429 antes de que lleguen a Billing, en vez de dejar caer el servicio."
      />

      <form className={`card ${styles.controles}`} onSubmit={lanzar} aria-label="Configuración de la ráfaga">
        <fieldset className={styles.escenarios} disabled={burst.corriendo}>
          <legend className="field-label">Escenario</legend>
          {(Object.keys(descripcionEscenario) as Escenario[]).map((valor) => {
            const disponible = disponibles.includes(valor);
            return (
              <label key={valor} className={`${styles.escenario} ${escenario === valor ? styles.escenarioActivo : ''}`}>
                <input type="radio" name="escenario" value={valor} checked={escenario === valor} disabled={!disponible} onChange={() => { setEscenario(valor); }} />
                <span className={styles.escenarioNombre}>{descripcionEscenario[valor].nombre}</span>
                <span className="muted small">
                  {disponible
                    ? descripcionEscenario[valor].detalle
                    : 'Su rol es Administración: tiene todos los permisos y no hay un endpoint que le responda 403.'}
                </span>
              </label>
            );
          })}
        </fieldset>
        <div className={styles.parametros}>
          <label className="field">
            <span className="field-label">Peticiones</span>
            <input className="input num" type="number" min={1} max={1000} value={peticiones} disabled={burst.corriendo} onChange={(event) => { setPeticiones(event.target.value); }} />
          </label>
          <label className="field">
            <span className="field-label">En segundos</span>
            <input className="input num" type="number" min={1} max={120} value={segundos} disabled={burst.corriendo} onChange={(event) => { setSegundos(event.target.value); }} />
          </label>
          {burst.corriendo ? (
            <button type="button" className="btn btn-secondary" onClick={burst.detener}>
              Detener
            </button>
          ) : (
            <button type="submit" className="btn btn-primary" disabled={Boolean(error)}>
              Lanzar ráfaga
            </button>
          )}
          <p className={`small ${error ? 'field-error' : 'muted'}`} role={error ? 'alert' : undefined}>
            {error ??
              `Destino: ${config.gatewayUrl}${escenario === 'fueraDeRol' ? (objetivoFueraDeRol(roles) ?? '') : '/api/billing/facturas'}. Límite: ${formatEntero(config.rateLimitPerMinute)} por minuto por usuario o IP.`}
          </p>
        </div>
      </form>

      <div className={styles.resumen}>
        <section className={`card ${styles.bloqueo}`} aria-live="polite" aria-labelledby="bloqueo-titulo">
          <p id="bloqueo-titulo" className={styles.bloqueoEtiqueta}>
            {restante > 0 ? 'El gateway vuelve a aceptar sus peticiones en' : 'Estado del límite'}
          </p>
          <p className={styles.hero}>{restante > 0 ? `${restante} s` : burst.bloqueadoHasta ? 'Libre' : 'Sin bloqueo'}</p>
          <p className="muted small">
            {restante > 0
              ? 'Cuenta regresiva del encabezado Retry-After de la última respuesta 429.'
              : burst.bloqueadoHasta
                ? 'La ventana del límite se reinició: puede lanzar otra ráfaga.'
                : 'Cuando el gateway responda 429, aquí verá cuánto falta para reintentar.'}
          </p>
          {burst.rafaga ? (
            <p className="small">
              {formatEntero(total)} de {formatEntero(burst.rafaga.peticiones)} respuestas
              {burst.corriendo ? ` (${formatEntero(burst.enviadas)} enviadas)` : ''}
            </p>
          ) : null}
        </section>
        <KpiRow compacta>
          {CLASES.map((clase) => (
            <KpiCard
              key={clase}
              etiqueta={`${etiquetaClase[clase].codigo} ${etiquetaClase[clase].corto}`}
              valor={formatEntero(conteo[clase])}
              nota={total ? `${porcentaje(conteo[clase], total)} % del total` : 'Sin respuestas todavía'}
              marca={claseSerie[clase]}
            />
          ))}
        </KpiRow>
      </div>

      <section className="card" aria-labelledby="grafica-titulo">
        <div className="card-header">
          <h2 id="grafica-titulo" className="card-title">
            Respuestas acumuladas por segundo
          </h2>
          <button type="button" className="btn btn-link small" onClick={() => { setTabla((valor) => !valor); }} aria-expanded={tabla}>
            {tabla ? 'Ver gráfica' : 'Ver como tabla'}
          </button>
        </div>
        <div className="card-body">
          {total === 0 && !burst.corriendo ? (
            <div className="empty">
              <p className="empty-title">Aún no hay respuestas</p>
              <p className="muted">Elija un escenario y pulse “Lanzar ráfaga”. Con 120 peticiones en 10 segundos verá el bloqueo después de la número {formatEntero(config.rateLimitPerMinute)}.</p>
            </div>
          ) : tabla ? (
            <div className="table-wrap">
              <table className="table">
                <caption className="visually-hidden">Respuestas acumuladas por segundo y clase</caption>
                <thead>
                  <tr>
                    <th scope="col">Segundo</th>
                    {CLASES.map((clase) => (
                      <th key={clase} scope="col" className="num">
                        {etiquetaClase[clase].codigo}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {serie.map((punto) => (
                    <tr key={punto.segundo}>
                      <td className="num">{punto.segundo}</td>
                      {CLASES.map((clase) => (
                        <td key={clase} className="num">
                          {formatEntero(punto.acumulado[clase])}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <>
              <ul className={styles.leyenda} aria-label="Leyenda">
                {CLASES.filter((clase) => conteo[clase] > 0).map((clase) => (
                  <li key={clase}>
                    <span className={`${styles.clave} ${claseSerie[clase]}`} aria-hidden="true" />
                    {etiquetaClase[clase].codigo} {etiquetaClase[clase].nombre}
                  </li>
                ))}
              </ul>
              <TrafficChart serie={serie} limite={config.rateLimitPerMinute} segundos={burst.rafaga?.segundos ?? 10} />
              <p className="muted small">
                El límite es una ventana fija de un minuto por usuario (o por IP sin token) y cuenta todas sus peticiones: las
                pantallas que abrió antes de la ráfaga también consumen cupo, por eso las aceptadas pueden quedar debajo de la
                línea.
              </p>
            </>
          )}
        </div>
      </section>

      <section className="card" aria-labelledby="registro-titulo">
        <div className="card-header">
          <h2 id="registro-titulo" className="card-title">
            Últimas respuestas
          </h2>
          <p className="muted small">El correlation id permite buscar cada petición en la auditoría y en los logs.</p>
        </div>
        {recientes.length === 0 ? (
          <p className="card-body muted">Las respuestas aparecerán aquí mientras corre la ráfaga.</p>
        ) : (
          <div className="table-wrap">
            <table className="table table-apilable">
              <thead>
                <tr>
                  <th scope="col" className="num">
                    #
                  </th>
                  <th scope="col">Respuesta</th>
                  <th scope="col" className="num">
                    Latencia
                  </th>
                  <th scope="col" className="num">
                    Retry-After
                  </th>
                  <th scope="col">Correlation id</th>
                </tr>
              </thead>
              <tbody>
                {recientes.map((resultado) => (
                  <tr key={resultado.indice}>
                    <td data-label="#" className="num">{resultado.indice + 1}</td>
                    <td data-label="Respuesta">
                      <span className={styles.respuesta}>
                        <span className={`${styles.clave} ${claseSerie[resultado.clase]}`} aria-hidden="true" />
                        {resultado.status ?? 'Sin respuesta'} {etiquetaClase[resultado.clase].nombre.toLowerCase()}
                      </span>
                    </td>
                    <td data-label="Latencia" className="num">{resultado.latenciaMs} ms</td>
                    <td data-label="Retry-After" className="num">{resultado.retryAfter !== null ? `${resultado.retryAfter} s` : ''}</td>
                    <td data-label="Correlation id" className="mono-id small">{resultado.correlationId}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      {roles.includes(Roles.Cliente) && roles.length === 1 ? (
        <p className="muted small">Las ráfagas con su sesión cuentan contra su propio límite: durante la espera, el resto del sistema también le responderá 429.</p>
      ) : null}
    </>
  );
}
