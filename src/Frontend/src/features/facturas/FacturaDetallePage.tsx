import { type SubmitEvent, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { billing } from '../../api/billing';
import { ApiError } from '../../api/http';
import type { FacturaDetalle, Obligado } from '../../api/types';
import { puede } from '../../auth/roles';
import { useSesion } from '../../auth/useSesion';
import { Dialog } from '../../components/Dialog';
import { EstadoFacturaBadge } from '../../components/EstadoBadge';
import { ErrorAlert } from '../../components/ErrorAlert';
import { useAvisar } from '../../components/toastContext';
import { aNumero, formatCantidad, formatFecha, formatFechaHora, formatLempiras } from '../../lib/format';
import { useApi } from '../../lib/useApi';
import { LineaCampos, type LineaBorrador } from './LineaCampos';
import styles from './Facturas.module.css';

const numeroDelRango = (obligado: Obligado, correlativo: number) => `${obligado.prefijo}-${String(correlativo).padStart(8, '0')}`;

export function FacturaDetallePage() {
  const { id = '' } = useParams();
  const { roles } = useSesion();
  const avisar = useAvisar();
  const factura = useApi(`factura:${id}`, (signal) => billing.factura(id, signal));
  const obligado = useApi('obligado', (signal) => billing.obligado(signal));
  const [error, setError] = useState<ApiError | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [dialogo, setDialogo] = useState<'emitir' | 'anular' | null>(null);
  const [motivo, setMotivo] = useState('');
  const [linea, setLinea] = useState<LineaBorrador>({ clave: 0, descripcion: '', cantidad: '1', precioUnitario: '', exento: false });
  const puedeEditar = puede(roles, 'gestionarFacturas');

  const ejecutar = async (accion: () => Promise<FacturaDetalle>, mensaje: string) => {
    setOcupado(true);
    setError(null);
    try {
      factura.reemplazar(await accion());
      avisar(mensaje);
      setDialogo(null);
      return true;
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : null);
      setDialogo(null);
      return false;
    } finally {
      setOcupado(false);
    }
  };

  const agregarLinea = async (event: SubmitEvent) => {
    event.preventDefault();
    const agregada = await ejecutar(
      () => billing.agregarLinea(id, { descripcion: linea.descripcion, cantidad: aNumero(linea.cantidad), precioUnitario: aNumero(linea.precioUnitario), exento: linea.exento }),
      'Línea agregada',
    );
    if (agregada) {
      setLinea({ clave: linea.clave + 1, descripcion: '', cantidad: '1', precioUnitario: '', exento: false });
    }
  };

  if (!factura.data) {
    return factura.error ? (
      <>
        <ErrorAlert error={factura.error} onReintentar={factura.recargar} />
        <Link to="/facturas">Volver a facturas</Link>
      </>
    ) : (
      <p className="muted" role="status">
        Cargando la factura…
      </p>
    );
  }

  const f = factura.data;
  const o = obligado.data;
  return (
    <>
      <nav className="small" aria-label="Ruta">
        <Link to="/facturas">Facturas</Link>
      </nav>
      <ErrorAlert error={error} />
      <div className={puedeEditar && f.estado !== 'Anulada' ? styles.layoutDetalle : `${styles.layoutDetalle} ${styles.layoutSolo}`}>
        <article className={`card ${styles.documento}`} aria-labelledby="numero-fiscal">
          <header className={styles.encabezado}>
            <div className={styles.emisor}>
              <p className={styles.emisorNombre}>{o?.razonSocial ?? 'Obligado tributario'}</p>
              {o ? <p className="muted small">RTN {o.rtn}</p> : null}
            </div>
            <div className={styles.numeroFiscal}>
              <p className={styles.numeroEtiqueta}>Factura</p>
              <p id="numero-fiscal" className={styles.numero}>
                {f.numero ?? 'Sin número fiscal'}
              </p>
              <EstadoFacturaBadge estado={f.estado} />
            </div>
          </header>
          <dl className={styles.datosFiscales}>
            <div className={styles.dato}>
              <dt>CAI</dt>
              <dd>{f.cai ?? o?.cai ?? '—'}</dd>
            </div>
            {o ? (
              <div className={styles.dato}>
                <dt>Rango autorizado</dt>
                <dd>
                  {numeroDelRango(o, o.rangoDesde)} al {numeroDelRango(o, o.rangoHasta)}
                </dd>
              </div>
            ) : null}
            {o ? (
              <div className={styles.dato}>
                <dt>Fecha límite de emisión</dt>
                <dd>{formatFecha(o.fechaLimiteEmision)}</dd>
              </div>
            ) : null}
            <div className={styles.dato}>
              <dt>Fecha de emisión</dt>
              <dd>{f.fechaEmision ? formatFecha(f.fechaEmision) : 'Pendiente'}</dd>
            </div>
            <div className={styles.dato}>
              <dt>Cliente</dt>
              <dd>
                {f.clienteNombre}
                {f.clienteRtn ? <span className="muted small"> (RTN {f.clienteRtn})</span> : null}
              </dd>
            </div>
            <div className={styles.dato}>
              <dt>Creada</dt>
              <dd>{formatFechaHora(f.creadaEn)}</dd>
            </div>
          </dl>
          {f.estado === 'Anulada' && f.motivoAnulacion ? (
            <p className={styles.motivo}>
              <strong>Anulada.</strong> Motivo: {f.motivoAnulacion}
            </p>
          ) : null}
          <div className="table-wrap">
            <table className="table table-apilable">
              <thead>
                <tr>
                  <th scope="col">Descripción</th>
                  <th scope="col" className="num">
                    Cantidad
                  </th>
                  <th scope="col" className="num">
                    Precio unitario
                  </th>
                  <th scope="col">ISV</th>
                  <th scope="col" className="num">
                    Importe
                  </th>
                  {puedeEditar && f.estado === 'Borrador' ? (
                    <th scope="col">
                      <span className="visually-hidden">Acciones</span>
                    </th>
                  ) : null}
                </tr>
              </thead>
              <tbody>
                {f.lineas.map((l) => (
                  <tr key={l.id}>
                    <td data-label="Descripción">{l.descripcion}</td>
                    <td data-label="Cantidad" className="num">{formatCantidad(l.cantidad)}</td>
                    <td data-label="Precio unitario" className="num">{formatLempiras(l.precioUnitario)}</td>
                    <td data-label="ISV">{l.exento ? 'Exento' : 'Gravado'}</td>
                    <td data-label="Importe" className="num">{formatLempiras(l.importe)}</td>
                    {puedeEditar && f.estado === 'Borrador' ? (
                      <td data-label="">
                        <button
                          type="button"
                          className="btn btn-link"
                          disabled={ocupado || f.lineas.length === 1}
                          title={f.lineas.length === 1 ? 'La factura debe conservar al menos una línea' : undefined}
                          onClick={() => void ejecutar(() => billing.quitarLinea(id, l.id), 'Línea quitada')}
                        >
                          Quitar
                        </button>
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <dl className={styles.totales}>
            <dt>Subtotal</dt>
            <dd>{formatLempiras(f.subtotal)}</dd>
            <dt>ISV 15 %</dt>
            <dd>{formatLempiras(f.isv)}</dd>
            <dt className={styles.totalFinal}>Total</dt>
            <dd className={styles.totalFinal}>{formatLempiras(f.total)}</dd>
          </dl>
        </article>

        {puedeEditar && f.estado !== 'Anulada' ? (
          <aside className={styles.acciones} aria-label="Acciones de la factura">
            {f.estado === 'Borrador' ? (
              <>
                <section className="card card-body form-grid">
                  <h2 className="card-title">Emitir</h2>
                  <p className="muted small">Asigna el siguiente número del rango del CAI y fija la fecha de hoy. Después no se pueden cambiar las líneas.</p>
                  <button type="button" className="btn btn-primary" disabled={ocupado} onClick={() => { setDialogo('emitir'); }}>
                    Emitir factura
                  </button>
                </section>
                <form className="card card-body form-grid" onSubmit={(event) => void agregarLinea(event)} aria-labelledby="titulo-linea">
                  <h2 id="titulo-linea" className="card-title">
                    Agregar línea
                  </h2>
                  <LineaCampos linea={linea} prefijo="" problema={error?.problem ?? null} onCambiar={setLinea} />
                  <button type="submit" className="btn btn-secondary" disabled={ocupado}>
                    Agregar línea
                  </button>
                </form>
              </>
            ) : (
              <section className="card card-body form-grid">
                <h2 className="card-title">Anular</h2>
                <p className="muted small">La anulación es definitiva y queda en la auditoría con su motivo.</p>
                <button type="button" className="btn btn-danger" disabled={ocupado} onClick={() => { setDialogo('anular'); }}>
                  Anular factura
                </button>
              </section>
            )}
          </aside>
        ) : null}
      </div>

      <Dialog
        abierto={dialogo === 'emitir'}
        titulo="¿Emitir esta factura?"
        confirmar="Emitir factura"
        ocupado={ocupado}
        onCerrar={() => { setDialogo(null); }}
        onConfirmar={() => void ejecutar(() => billing.emitir(id), 'Factura emitida')}
      >
        <p>
          Se le asignará el número {o ? numeroDelRango(o, o.siguienteCorrelativo) : 'siguiente del rango'} por {formatLempiras(f.total)}.
        </p>
      </Dialog>
      <Dialog
        abierto={dialogo === 'anular'}
        titulo="Anular factura"
        confirmar="Anular factura"
        tono="danger"
        ocupado={ocupado}
        onCerrar={() => { setDialogo(null); }}
        onConfirmar={() => void ejecutar(() => billing.anular(id, motivo), 'Factura anulada')}
      >
        <label className="field">
          <span className="field-label">Motivo de la anulación</span>
          <textarea className="input" value={motivo} maxLength={250} required onChange={(event) => { setMotivo(event.target.value); }} />
          <span className="field-hint">Queda registrado en la factura y en la auditoría.</span>
        </label>
      </Dialog>
    </>
  );
}
