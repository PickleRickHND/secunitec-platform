import { useState } from 'react';
import { billing, type FiltroAuditoria } from '../../api/billing';
import type { ResultadoAuditoria } from '../../api/types';
import { ResultadoBadge } from '../../components/EstadoBadge';
import { ErrorAlert } from '../../components/ErrorAlert';
import { PageHeader } from '../../components/PageHeader';
import { Pagination } from '../../components/Pagination';
import { formatFechaHora } from '../../lib/format';
import { useApi } from '../../lib/useApi';
import styles from '../facturas/Facturas.module.css';

const TAMANO = 50;

// Acciones que registran Billing (AccionesAuditoria) e Identity (IdentityAuditWriter).
const acciones: Record<string, string> = {
  'factura.creada': 'Factura creada',
  'factura.linea_agregada': 'Línea agregada',
  'factura.linea_quitada': 'Línea quitada',
  'factura.emitida': 'Factura emitida',
  'factura.anulada': 'Factura anulada',
  'cliente.creado': 'Cliente registrado',
  'cliente.actualizado': 'Cliente actualizado',
  'obligado.creado': 'Obligado registrado',
  'obligado.cai_actualizado': 'CAI registrado',
  'obligado.activado': 'Emisión activada',
  'obligado.desactivado': 'Emisión desactivada',
  'acceso.denegado': 'Acceso denegado',
  login: 'Inicio de sesión',
  login_locked_out: 'Cuenta bloqueada',
  register: 'Registro de cuenta',
  logout: 'Cierre de sesión',
};

// Detalles que registra Identity (IdentityAuditWriter); los de Billing ya vienen en español.
const detalles: Record<string, string> = {
  success: 'Correcto',
  invalid_credentials: 'Credenciales incorrectas',
  locked_out: 'Cuenta bloqueada por intentos fallidos',
};

export function AuditoriaPage() {
  const [filtro, setFiltro] = useState<FiltroAuditoria>({ pagina: 1, tamano: TAMANO });
  const clave = `auditoria:${JSON.stringify(filtro)}`;
  const eventos = useApi(clave, (signal) => billing.auditoria(filtro, signal));
  const cambiar = (cambios: Partial<FiltroAuditoria>) => {
    setFiltro((actual) => ({ ...actual, ...cambios, pagina: cambios.pagina ?? 1 }));
  };

  return (
    <>
      <PageHeader
        titulo="Auditoría"
        descripcion="Cada operación, acceso denegado e inicio de sesión de su empresa. Los eventos solo se agregan: nadie puede editarlos ni borrarlos."
      />
      <form className={styles.filtros} aria-label="Filtros" onSubmit={(event) => { event.preventDefault(); }}>
        <label className="field">
          <span className="field-label">Acción</span>
          <select className="input" value={filtro.accion ?? ''} onChange={(event) => { cambiar({ accion: event.target.value || null }); }}>
            <option value="">Todas</option>
            {Object.entries(acciones).map(([valor, texto]) => (
              <option key={valor} value={valor}>
                {texto}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span className="field-label">Resultado</span>
          <select className="input" value={filtro.resultado ?? ''} onChange={(event) => { cambiar({ resultado: (event.target.value || null) as ResultadoAuditoria | null }); }}>
            <option value="">Todos</option>
            <option value="Exito">Éxito</option>
            <option value="Denegado">Denegado</option>
            <option value="Fallido">Fallido</option>
          </select>
        </label>
        <label className="field">
          <span className="field-label">Desde</span>
          <input className="input" type="date" value={filtro.desde ?? ''} onChange={(event) => { cambiar({ desde: event.target.value || null }); }} />
        </label>
        <label className="field">
          <span className="field-label">Hasta</span>
          <input className="input" type="date" value={filtro.hasta ?? ''} onChange={(event) => { cambiar({ hasta: event.target.value || null }); }} />
        </label>
      </form>
      <ErrorAlert error={eventos.error} onReintentar={eventos.recargar} />
      <section className={`card ${eventos.cargando && eventos.data ? styles.recargando : ''}`} aria-busy={eventos.cargando}>
        {eventos.data && eventos.data.items.length === 0 ? (
          <div className="empty">
            <p className="empty-title">No hay eventos con estos filtros</p>
            <p className="muted">Amplíe el rango de fechas o quite el filtro de acción.</p>
          </div>
        ) : (
          <div className="table-wrap">
            <table className="table table-apilable">
              <thead>
                <tr>
                  <th scope="col">Fecha y hora</th>
                  <th scope="col">Acción</th>
                  <th scope="col">Resultado</th>
                  <th scope="col">Origen</th>
                  <th scope="col">IP</th>
                  <th scope="col">Detalle</th>
                  <th scope="col">Correlation id</th>
                </tr>
              </thead>
              <tbody>
                {eventos.data?.items.map((evento) => (
                  <tr key={`${evento.origen}-${evento.id}`}>
                    <td data-label="Fecha y hora" className="num">{formatFechaHora(evento.timestamp)}</td>
                    <td data-label="Acción">{acciones[evento.accion] ?? evento.accion}</td>
                    <td data-label="Resultado">
                      <ResultadoBadge resultado={evento.resultado} />
                    </td>
                    <td data-label="Origen">{evento.origen === 'identity' ? 'Identity' : 'Billing'}</td>
                    <td data-label="IP" className="nowrap">{evento.ip ?? ''}</td>
                    <td data-label="Detalle" className="small">{(evento.detalle && detalles[evento.detalle]) ?? evento.detalle ?? evento.recurso ?? ''}</td>
                    <td data-label="Correlation id" className="mono-id small">{evento.correlationId ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {eventos.data ? (
          <Pagination pagina={filtro.pagina ?? 1} tamano={TAMANO} total={eventos.data.total} onCambiar={(pagina) => { cambiar({ pagina }); }} />
        ) : null}
      </section>
    </>
  );
}
