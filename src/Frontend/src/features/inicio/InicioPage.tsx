import { Link } from 'react-router-dom';
import { billing } from '../../api/billing';
import type { EstadoFactura } from '../../api/types';
import { puede, Roles } from '../../auth/roles';
import { useSesion } from '../../auth/useSesion';
import { EstadoFacturaBadge } from '../../components/EstadoBadge';
import { ErrorAlert } from '../../components/ErrorAlert';
import { KpiCard, KpiRow } from '../../components/KpiCard';
import { PageHeader } from '../../components/PageHeader';
import { formatEntero, formatFecha, formatLempiras } from '../../lib/format';
import { useApi } from '../../lib/useApi';

const estados: EstadoFactura[] = ['Borrador', 'Emitida', 'Anulada'];

export function InicioPage() {
  const { roles } = useSesion();
  // El total de cada estado sale del mismo listado paginado (una fila por consulta).
  const conteos = useApi('inicio-conteos', (signal) =>
    Promise.all(estados.map((estado) => billing.facturas({ estado, tamano: 1 }, signal).then((pagina) => pagina.total))));
  const recientes = useApi('inicio-recientes', (signal) => billing.facturas({ tamano: 5 }, signal));
  const esCliente = roles.length === 1 && roles[0] === Roles.Cliente;

  return (
    <>
      <PageHeader
        titulo="Inicio"
        descripcion={esCliente ? 'Las facturas que su proveedor le ha emitido.' : 'Resumen de la facturación de su empresa.'}
        acciones={
          puede(roles, 'gestionarFacturas') ? (
            <Link to="/facturas/nueva" className="btn btn-primary">
              Nueva factura
            </Link>
          ) : null
        }
      />
      <ErrorAlert error={conteos.error ?? recientes.error} onReintentar={conteos.recargar} />
      <KpiRow>
        {estados.map((estado, indice) => (
          <KpiCard
            key={estado}
            etiqueta={estado === 'Borrador' ? 'Borradores' : estado === 'Emitida' ? 'Emitidas' : 'Anuladas'}
            valor={conteos.data ? formatEntero(conteos.data[indice] ?? 0) : '—'}
            nota={estado === 'Borrador' ? 'Aún sin número fiscal' : estado === 'Emitida' ? 'Con número y CAI' : 'Con motivo registrado'}
          />
        ))}
      </KpiRow>
      <section className="card" aria-labelledby="recientes">
        <div className="card-header">
          <h2 id="recientes" className="card-title">
            Últimas facturas
          </h2>
          <Link to="/facturas" className="small">
            Ver todas
          </Link>
        </div>
        {recientes.data && recientes.data.items.length === 0 ? (
          <div className="empty">
            <p className="empty-title">Todavía no hay facturas</p>
            <p className="muted">{puede(roles, 'gestionarFacturas') ? 'Cree la primera con “Nueva factura”.' : 'Aparecerán aquí cuando se emitan.'}</p>
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
                    Total
                  </th>
                </tr>
              </thead>
              <tbody>
                {recientes.data?.items.map((factura) => (
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
                    <td data-label="Total" className="num">{formatLempiras(factura.total)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}
