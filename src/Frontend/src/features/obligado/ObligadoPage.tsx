import { useState } from 'react';
import { billing } from '../../api/billing';
import { ApiError } from '../../api/http';
import { fieldError } from '../../api/problem';
import type { ActualizarCai, Obligado } from '../../api/types';
import { puede } from '../../auth/roles';
import { useSesion } from '../../auth/useSesion';
import { Dialog } from '../../components/Dialog';
import { ErrorAlert } from '../../components/ErrorAlert';
import { PageHeader } from '../../components/PageHeader';
import { useAvisar } from '../../components/toastContext';
import { formatEntero, formatFecha } from '../../lib/format';
import { useApi } from '../../lib/useApi';
import styles from '../facturas/Facturas.module.css';

const numero = (obligado: Obligado, correlativo: number) => `${obligado.prefijo}-${String(correlativo).padStart(8, '0')}`;

export function ObligadoPage() {
  const { roles } = useSesion();
  const avisar = useAvisar();
  const obligado = useApi('obligado', (signal) => billing.obligado(signal));
  const [dialogo, setDialogo] = useState<'cai' | 'desactivar' | null>(null);
  const [cai, setCai] = useState<ActualizarCai>({ cai: '', prefijo: '000-001-01', rangoDesde: 1, rangoHasta: 1000, fechaLimiteEmision: '' });
  const [error, setError] = useState<ApiError | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const esAdmin = puede(roles, 'gestionarObligado');

  const ejecutar = async (accion: () => Promise<Obligado>, mensaje: string) => {
    setOcupado(true);
    setError(null);
    try {
      obligado.reemplazar(await accion());
      avisar(mensaje);
      setDialogo(null);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : null);
    } finally {
      setOcupado(false);
    }
  };

  const o = obligado.data;
  const campo = (nombre: keyof ActualizarCai) => fieldError(error?.problem, nombre);
  return (
    <>
      <PageHeader
        titulo="Obligado tributario"
        descripcion="Los datos fiscales con los que su empresa emite: RTN, CAI vigente y rango autorizado por el SAR."
        acciones={
          esAdmin && o ? (
            <>
              <button type="button" className="btn btn-primary" onClick={() => { setError(null); setCai({ ...cai, prefijo: o.prefijo }); setDialogo('cai'); }}>
                Registrar CAI nuevo
              </button>
              {o.activo ? (
                <button type="button" className="btn btn-danger" onClick={() => { setDialogo('desactivar'); }}>
                  Desactivar emisión
                </button>
              ) : (
                <button type="button" className="btn btn-secondary" disabled={ocupado} onClick={() => void ejecutar(() => billing.activarObligado(), 'Emisión activada')}>
                  Activar emisión
                </button>
              )}
            </>
          ) : null
        }
      />
      {obligado.error?.status === 404 ? (
        <div className="card empty">
          <p className="empty-title">Su empresa aún no tiene un obligado tributario registrado</p>
          <p className="muted">Un administrador debe registrarlo antes de crear clientes o facturas.</p>
        </div>
      ) : (
        <ErrorAlert error={obligado.error ?? (dialogo ? null : error)} onReintentar={obligado.recargar} />
      )}
      {o ? (
        <section className={`card ${styles.documento}`} aria-labelledby="razon-social">
          <header className={styles.encabezado}>
            <div className={styles.emisor}>
              <h2 id="razon-social" className={styles.emisorNombre}>
                {o.razonSocial}
              </h2>
              <p className="muted small">RTN {o.rtn}</p>
            </div>
            <span className={`badge ${o.activo ? 'badge-ok' : 'badge-danger'}`}>{o.activo ? 'Emisión activa' : 'Emisión desactivada'}</span>
          </header>
          <dl className={styles.datosFiscales}>
            <div className={styles.dato}>
              <dt>CAI vigente</dt>
              <dd>{o.cai}</dd>
            </div>
            <div className={styles.dato}>
              <dt>Rango autorizado</dt>
              <dd>
                {numero(o, o.rangoDesde)} al {numero(o, o.rangoHasta)}
              </dd>
            </div>
            <div className={styles.dato}>
              <dt>Siguiente número</dt>
              <dd>{o.siguienteCorrelativo > o.rangoHasta ? 'Rango agotado' : numero(o, o.siguienteCorrelativo)}</dd>
            </div>
            <div className={styles.dato}>
              <dt>Números disponibles</dt>
              <dd>{formatEntero(Math.max(0, o.rangoHasta - o.siguienteCorrelativo + 1))}</dd>
            </div>
            <div className={styles.dato}>
              <dt>Fecha límite de emisión</dt>
              <dd>{formatFecha(o.fechaLimiteEmision)}</dd>
            </div>
          </dl>
        </section>
      ) : null}

      <Dialog
        abierto={dialogo === 'cai'}
        titulo="Registrar CAI nuevo"
        confirmar="Registrar CAI"
        ocupado={ocupado}
        onCerrar={() => { setDialogo(null); }}
        onConfirmar={() => void ejecutar(() => billing.actualizarCai(cai), 'CAI registrado')}
      >
        <p className="muted small">El correlativo vuelve al inicio del rango nuevo. Si el rango repite números ya emitidos con el mismo prefijo, el servidor lo rechaza.</p>
        <ErrorAlert error={error?.problem.errors ? null : error} />
        <label className="field">
          <span className="field-label">CAI</span>
          <input className="input" value={cai.cai} required placeholder="XXXXXX-XXXXXX-XXXXXX-XXXXXX-XXXXXX-XX" onChange={(event) => { setCai({ ...cai, cai: event.target.value.toUpperCase() }); }} />
          {campo('cai') ? <span className="field-error">{campo('cai')}</span> : null}
        </label>
        <label className="field">
          <span className="field-label">Prefijo</span>
          <input className="input" value={cai.prefijo} required onChange={(event) => { setCai({ ...cai, prefijo: event.target.value }); }} />
          {campo('prefijo') ? <span className="field-error">{campo('prefijo')}</span> : null}
        </label>
        <div className="row">
          <label className="field">
            <span className="field-label">Rango desde</span>
            <input className="input num" type="number" min={1} value={cai.rangoDesde} onChange={(event) => { setCai({ ...cai, rangoDesde: Number(event.target.value) }); }} />
          </label>
          <label className="field">
            <span className="field-label">Rango hasta</span>
            <input className="input num" type="number" min={1} value={cai.rangoHasta} onChange={(event) => { setCai({ ...cai, rangoHasta: Number(event.target.value) }); }} />
          </label>
        </div>
        {campo('rangoHasta') ? <span className="field-error">{campo('rangoHasta')}</span> : null}
        <label className="field">
          <span className="field-label">Fecha límite de emisión</span>
          <input className="input" type="date" required value={cai.fechaLimiteEmision} onChange={(event) => { setCai({ ...cai, fechaLimiteEmision: event.target.value }); }} />
        </label>
      </Dialog>
      <Dialog
        abierto={dialogo === 'desactivar'}
        titulo="¿Desactivar la emisión?"
        confirmar="Desactivar emisión"
        tono="danger"
        ocupado={ocupado}
        onCerrar={() => { setDialogo(null); }}
        onConfirmar={() => void ejecutar(() => billing.desactivarObligado(), 'Emisión desactivada')}
      >
        <p>Nadie de su empresa podrá emitir facturas hasta que la vuelva a activar. Los borradores se conservan.</p>
      </Dialog>
    </>
  );
}
