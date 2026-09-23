import { type SubmitEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { billing } from '../../api/billing';
import { ApiError } from '../../api/http';
import { fieldError, type Problem } from '../../api/problem';
import { ErrorAlert } from '../../components/ErrorAlert';
import { PageHeader } from '../../components/PageHeader';
import { useAvisar } from '../../components/toastContext';
import { aNumero } from '../../lib/format';
import { useApi } from '../../lib/useApi';
import { LineaCampos, type LineaBorrador } from './LineaCampos';
import styles from './Facturas.module.css';

let claves = 0;
const lineaVacia = (): LineaBorrador => ({ clave: ++claves, descripcion: '', cantidad: '1', precioUnitario: '', exento: false });

export function FacturaNuevaPage() {
  const navigate = useNavigate();
  const avisar = useAvisar();
  const clientes = useApi('clientes-select', (signal) => billing.clientes('', 1, signal, 100));
  const [clienteId, setClienteId] = useState('');
  const [lineas, setLineas] = useState<LineaBorrador[]>(() => [lineaVacia()]);
  const [error, setError] = useState<ApiError | null>(null);
  const [guardando, setGuardando] = useState(false);
  const problema: Problem | null = error?.problem ?? null;

  const guardar = async (event: SubmitEvent) => {
    event.preventDefault();
    setGuardando(true);
    setError(null);
    try {
      const factura = await billing.crearFactura(
        clienteId,
        lineas.map((linea) => ({
          descripcion: linea.descripcion,
          cantidad: aNumero(linea.cantidad),
          precioUnitario: aNumero(linea.precioUnitario),
          exento: linea.exento,
        })),
      );
      avisar('Borrador guardado');
      void navigate(`/facturas/${factura.id}`);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : null);
    } finally {
      setGuardando(false);
    }
  };

  return (
    <>
      <PageHeader titulo="Nueva factura" descripcion="Se guarda como borrador. El número fiscal se asigna al emitirla." />
      <form className="card" onSubmit={(event) => void guardar(event)} noValidate={false}>
        <div className="card-body form-grid">
          <ErrorAlert error={error} />
          <div className="field">
            <label className="field-label" htmlFor="cliente">
              Cliente
            </label>
            <select
              id="cliente"
              className="input"
              value={clienteId}
              required
              aria-invalid={Boolean(fieldError(problema, 'clienteId'))}
              onChange={(event) => { setClienteId(event.target.value); }}
            >
              <option value="">Elija un cliente</option>
              {clientes.data?.items.map((cliente) => (
                <option key={cliente.id} value={cliente.id}>
                  {cliente.nombre}
                  {cliente.rtn ? ` (RTN ${cliente.rtn})` : ''}
                </option>
              ))}
            </select>
            {clientes.data?.items.length === 0 ? (
              <span className="field-hint">
                Aún no hay clientes. <Link to="/clientes">Registre uno primero</Link>.
              </span>
            ) : null}
          </div>
          <fieldset className={styles.lineas}>
            <legend className="field-label">Líneas</legend>
            {lineas.map((linea, indice) => (
              <LineaCampos
                key={linea.clave}
                linea={linea}
                prefijo={`lineas[${indice}]`}
                problema={problema}
                onCambiar={(cambiada) => { setLineas((actuales) => actuales.map((actual) => (actual.clave === cambiada.clave ? cambiada : actual))); }}
                onQuitar={lineas.length > 1 ? () => { setLineas((actuales) => actuales.filter((actual) => actual.clave !== linea.clave)); } : undefined}
              />
            ))}
            <div className="row">
              <button type="button" className="btn btn-secondary" onClick={() => { setLineas((actuales) => [...actuales, lineaVacia()]); }}>
                Agregar línea
              </button>
              <p className={styles.nota}>El ISV (15 % sobre las líneas no exentas) y los totales los calcula el servidor al guardar.</p>
            </div>
          </fieldset>
        </div>
        <div className="dialog-actions">
          <Link to="/facturas" className="btn btn-secondary">
            Cancelar
          </Link>
          <button type="submit" className="btn btn-primary" disabled={guardando}>
            {guardando ? 'Guardando…' : 'Guardar borrador'}
          </button>
        </div>
      </form>
    </>
  );
}
