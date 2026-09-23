import { Link, useSearchParams } from 'react-router-dom';
import { billing } from '../../api/billing';
import type { EstadoFactura } from '../../api/types';
import { puede } from '../../auth/roles';
import { useSesion } from '../../auth/useSesion';
import { EstadoFacturaBadge } from '../../components/EstadoBadge';
import { ErrorAlert } from '../../components/ErrorAlert';
import { PageHeader } from '../../components/PageHeader';
import { Pagination } from '../../components/Pagination';
import { formatFecha, formatLempiras } from '../../lib/format';
import { useApi } from '../../lib/useApi';
import styles from './Facturas.module.css';

const TAMANO = 20;

export function FacturasPage() {
  const { roles } = useSesion();
  const [params, setParams] = useSearchParams();
  const estado = (params.get('estado') as EstadoFactura | null) ?? null;
  const desde = params.get('desde');
  const hasta = params.get('hasta');
  const pagina = Number(params.get('pagina') ?? '1') || 1;
  const clave = `facturas:${estado ?? ''}:${desde ?? ''}:${hasta ?? ''}:${pagina}`;
  const facturas = useApi(clave, (signal) => billing.facturas({ estado, desde, hasta, pagina, tamano: TAMANO }, signal));

  const cambiar = (cambios: Record<string, string | null>) => {
    const siguiente = new URLSearchParams(params);
    for (const [key, value] of Object.entries(cambios)) {
      if (value) {
        siguiente.set(key, value);
      } else {
        siguiente.delete(key);
      }
    }

    if (!('pagina' in cambios)) {
      siguiente.delete('pagina');
    }

    setParams(siguiente);
  };

  return (
    <>
      <PageHeader
        titulo="Facturas"
        descripcion="Los totales, el ISV y el número fiscal los calcula el servidor."
        acciones={
          puede(roles, 'gestionarFacturas') ? (
            <Link to="/facturas/nueva" className="btn btn-primary">
              Nueva factura
            </Link>
          ) : null
        }
      />
      <form className={styles.filtros} aria-label="Filtros" onSubmit={(event) => { event.preventDefault(); }}>
          <label className="field">
            <span className="field-label">Estado</span>
            <select className="input" value={estado ?? ''} onChange={(event) => { cambiar({ estado: event.target.value || null }); }}>
              <option value="">Todos</option>
              <option value="Borrador">Borrador</option>
              <option value="Emitida">Emitida</option>
              <option value="Anulada">Anulada</option>
            </select>
          </label>
          <label className="field">
            <span className="field-label">Emitidas desde</span>
            <input className="input" type="date" value={desde ?? ''} onChange={(event) => { cambiar({ desde: event.target.value || null }); }} />
          </label>
          <label className="field">
            <span className="field-label">Hasta</span>
            <input className="input" type="date" value={hasta ?? ''} onChange={(event) => { cambiar({ hasta: event.target.value || null }); }} />
          </label>
      </form>
      <ErrorAlert error={facturas.error} onReintentar={facturas.recargar} />
      <section className={`card ${facturas.cargando && facturas.data ? styles.recargando : ''}`} aria-busy={facturas.cargando}>
        {facturas.data && facturas.data.items.length === 0 ? (
          <div className="empty">
            <p className="empty-title">No hay facturas con estos filtros</p>
            <p className="muted">Cambie el estado o las fechas. Las fechas filtran por la fecha de emisión: los borradores no la tienen.</p>
          </div>
        ) : (
          <div className="table-wrap">
            <table className="table table-apilable">
              <thead>
                <tr>
                  <th scope="col">Número</th>
                  <th scope="col">Cliente</th>
                  <th scope="col">Estado</th>
                  <th scope="col">Emisión</th>
                  <th scope="col" className="num">
                    Subtotal
                  </th>
                  <th scope="col" className="num">
                    ISV
                  </th>
                  <th scope="col" className="num">
                    Total
                  </th>
                </tr>
              </thead>
              <tbody>
                {facturas.data?.items.map((factura) => (
                  <tr key={factura.id}>
                    <td data-label="Número">
                      <Link to={`/facturas/${factura.id}`} className="mono-id">
                        {factura.numero ?? 'Borrador sin número'}
                      </Link>
                    </td>
                    <td data-label="Cliente">{factura.clienteNombre}</td>
                    <td data-label="Estado">
                      <EstadoFacturaBadge estado={factura.estado} />
                    </td>
                    <td data-label="Emisión" className="num">{formatFecha(factura.fechaEmision)}</td>
                    <td data-label="Subtotal" className="num">{formatLempiras(factura.subtotal)}</td>
                    <td data-label="ISV" className="num">{formatLempiras(factura.isv)}</td>
                    <td data-label="Total" className="num">{formatLempiras(factura.total)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {facturas.data ? (
          <Pagination pagina={pagina} tamano={TAMANO} total={facturas.data.total} onCambiar={(nueva) => { cambiar({ pagina: String(nueva) }); }} />
        ) : null}
      </section>
    </>
  );
}
