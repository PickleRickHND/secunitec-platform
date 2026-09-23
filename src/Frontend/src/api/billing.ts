// Endpoints de Billing a través del gateway (/api/billing/...).
import { api } from './http';
import type {
  ActualizarCai,
  Cliente,
  DatosCliente,
  EstadoFactura,
  EventoAuditoria,
  FacturaDetalle,
  FacturaResumen,
  NuevaLinea,
  Obligado,
  Pagina,
  ResultadoAuditoria,
} from './types';

const query = (params: Record<string, string | number | null | undefined>): string => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') {
      search.set(key, String(value));
    }
  }

  const text = search.toString();
  return text ? `?${text}` : '';
};

export interface FiltroFacturas {
  estado?: EstadoFactura | null;
  clienteId?: string | null;
  desde?: string | null;
  hasta?: string | null;
  pagina?: number;
  tamano?: number;
}

export interface FiltroAuditoria {
  accion?: string | null;
  resultado?: ResultadoAuditoria | null;
  desde?: string | null;
  hasta?: string | null;
  pagina?: number;
  tamano?: number;
}

export const billing = {
  facturas: (filtro: FiltroFacturas, signal?: AbortSignal) =>
    api<Pagina<FacturaResumen>>(`/api/billing/facturas${query({ ...filtro })}`, { signal }),
  factura: (id: string, signal?: AbortSignal) => api<FacturaDetalle>(`/api/billing/facturas/${id}`, { signal }),
  crearFactura: (clienteId: string, lineas: NuevaLinea[]) =>
    api<FacturaDetalle>('/api/billing/facturas', { method: 'POST', body: { clienteId, lineas } }),
  agregarLinea: (id: string, linea: NuevaLinea) =>
    api<FacturaDetalle>(`/api/billing/facturas/${id}/lineas`, { method: 'POST', body: linea }),
  quitarLinea: (id: string, lineaId: number) =>
    api<FacturaDetalle>(`/api/billing/facturas/${id}/lineas/${lineaId}`, { method: 'DELETE' }),
  emitir: (id: string) => api<FacturaDetalle>(`/api/billing/facturas/${id}/emitir`, { method: 'POST' }),
  anular: (id: string, motivo: string) =>
    api<FacturaDetalle>(`/api/billing/facturas/${id}/anular`, { method: 'POST', body: { motivo } }),

  clientes: (buscar: string, pagina: number, signal?: AbortSignal, tamano = 20) =>
    api<Pagina<Cliente>>(`/api/billing/clientes${query({ buscar, pagina, tamano })}`, { signal }),
  crearCliente: (datos: DatosCliente) => api<Cliente>('/api/billing/clientes', { method: 'POST', body: datos }),
  actualizarCliente: (id: string, datos: DatosCliente) =>
    api<Cliente>(`/api/billing/clientes/${id}`, { method: 'PUT', body: datos }),

  obligado: (signal?: AbortSignal) => api<Obligado>('/api/billing/obligados/actual', { signal }),
  actualizarCai: (datos: ActualizarCai) => api<Obligado>('/api/billing/obligados/actual/cai', { method: 'PUT', body: datos }),
  activarObligado: () => api<Obligado>('/api/billing/obligados/actual/activar', { method: 'POST' }),
  desactivarObligado: () => api<Obligado>('/api/billing/obligados/actual/desactivar', { method: 'POST' }),

  auditoria: (filtro: FiltroAuditoria, signal?: AbortSignal) =>
    api<Pagina<EventoAuditoria>>(`/api/billing/auditoria${query({ ...filtro })}`, { signal }),
};
