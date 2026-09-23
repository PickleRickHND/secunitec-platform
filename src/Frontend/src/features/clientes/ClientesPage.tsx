import { useState } from 'react';
import { billing } from '../../api/billing';
import { ApiError } from '../../api/http';
import { fieldError } from '../../api/problem';
import type { Cliente, DatosCliente } from '../../api/types';
import { Dialog } from '../../components/Dialog';
import { ErrorAlert } from '../../components/ErrorAlert';
import { PageHeader } from '../../components/PageHeader';
import { Pagination } from '../../components/Pagination';
import { useAvisar } from '../../components/toastContext';
import { useApi } from '../../lib/useApi';

const TAMANO = 20;
const vacio: DatosCliente = { nombre: '', rtn: null, email: null };

export function ClientesPage() {
  const avisar = useAvisar();
  const [buscar, setBuscar] = useState('');
  const [pagina, setPagina] = useState(1);
  const clientes = useApi(`clientes:${buscar}:${pagina}`, (signal) => billing.clientes(buscar, pagina, signal, TAMANO));
  const [editando, setEditando] = useState<{ id: string | null; datos: DatosCliente } | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [ocupado, setOcupado] = useState(false);

  const abrir = (cliente?: Cliente) => {
    setError(null);
    setEditando(cliente ? { id: cliente.id, datos: { nombre: cliente.nombre, rtn: cliente.rtn, email: cliente.email } } : { id: null, datos: vacio });
  };

  const guardar = async () => {
    if (!editando) {
      return;
    }

    setOcupado(true);
    setError(null);
    try {
      const datos = { ...editando.datos, rtn: editando.datos.rtn?.trim() || null, email: editando.datos.email?.trim() || null };
      if (editando.id) {
        await billing.actualizarCliente(editando.id, datos);
        avisar('Cliente actualizado');
      } else {
        await billing.crearCliente(datos);
        avisar('Cliente registrado');
      }

      setEditando(null);
      clientes.recargar();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : null);
    } finally {
      setOcupado(false);
    }
  };

  const cambiar = (campo: keyof DatosCliente, valor: string) => {
    setEditando((actual) => (actual ? { ...actual, datos: { ...actual.datos, [campo]: valor } } : actual));
  };

  return (
    <>
      <PageHeader
        titulo="Clientes"
        descripcion="Los clientes a los que su empresa factura."
        acciones={
          <button type="button" className="btn btn-primary" onClick={() => { abrir(); }}>
            Nuevo cliente
          </button>
        }
      />
      <form role="search" className="row" onSubmit={(event) => { event.preventDefault(); }}>
        <label className="field">
          <span className="field-label">Buscar</span>
          <input
            className="input"
            type="search"
            placeholder="Nombre, correo o RTN"
            value={buscar}
            maxLength={100}
            onChange={(event) => {
              setBuscar(event.target.value);
              setPagina(1);
            }}
          />
        </label>
      </form>
      <ErrorAlert error={clientes.error} onReintentar={clientes.recargar} />
      <section className="card" aria-busy={clientes.cargando}>
        {clientes.data && clientes.data.items.length === 0 ? (
          <div className="empty">
            <p className="empty-title">{buscar ? 'Ningún cliente coincide con la búsqueda' : 'Todavía no hay clientes'}</p>
            <p className="muted">{buscar ? 'Pruebe con otra parte del nombre o el RTN completo.' : 'Registre el primero con “Nuevo cliente”.'}</p>
          </div>
        ) : (
          <div className="table-wrap">
            <table className="table table-apilable">
              <thead>
                <tr>
                  <th scope="col">Nombre</th>
                  <th scope="col">RTN</th>
                  <th scope="col">Correo</th>
                  <th scope="col">
                    <span className="visually-hidden">Acciones</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {clientes.data?.items.map((cliente) => (
                  <tr key={cliente.id}>
                    <td data-label="Nombre">{cliente.nombre}</td>
                    <td data-label="RTN" className="mono-id">{cliente.rtn ?? <span className="muted">Sin RTN</span>}</td>
                    <td data-label="Correo">{cliente.email ?? <span className="muted">Sin correo</span>}</td>
                    <td data-label="">
                      <button type="button" className="btn btn-link" onClick={() => { abrir(cliente); }}>
                        Editar
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {clientes.data ? <Pagination pagina={pagina} tamano={TAMANO} total={clientes.data.total} onCambiar={setPagina} /> : null}
      </section>

      <Dialog
        abierto={editando !== null}
        titulo={editando?.id ? 'Editar cliente' : 'Nuevo cliente'}
        confirmar={editando?.id ? 'Guardar cambios' : 'Registrar cliente'}
        ocupado={ocupado}
        onCerrar={() => { setEditando(null); }}
        onConfirmar={() => void guardar()}
      >
        <ErrorAlert error={error?.problem.errors ? null : error} />
        <label className="field">
          <span className="field-label">Nombre o razón social</span>
          <input className="input" value={editando?.datos.nombre ?? ''} maxLength={250} required onChange={(event) => { cambiar('nombre', event.target.value); }} />
          {fieldError(error?.problem, 'nombre') ? <span className="field-error">{fieldError(error?.problem, 'nombre')}</span> : null}
        </label>
        <label className="field">
          <span className="field-label">RTN (opcional)</span>
          <input
            className="input"
            inputMode="numeric"
            value={editando?.datos.rtn ?? ''}
            maxLength={14}
            aria-invalid={Boolean(fieldError(error?.problem, 'rtn'))}
            onChange={(event) => { cambiar('rtn', event.target.value); }}
          />
          <span className="field-hint">14 dígitos, sin guiones.</span>
          {fieldError(error?.problem, 'rtn') ? <span className="field-error">{fieldError(error?.problem, 'rtn')}</span> : null}
        </label>
        <label className="field">
          <span className="field-label">Correo (opcional)</span>
          <input
            className="input"
            type="email"
            value={editando?.datos.email ?? ''}
            maxLength={250}
            aria-invalid={Boolean(fieldError(error?.problem, 'email'))}
            onChange={(event) => { cambiar('email', event.target.value); }}
          />
          {fieldError(error?.problem, 'email') ? <span className="field-error">{fieldError(error?.problem, 'email')}</span> : null}
        </label>
      </Dialog>
    </>
  );
}
