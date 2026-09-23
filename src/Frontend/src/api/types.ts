// DTOs de Billing (src/Billing/Secunitec.Billing.Application/Contracts.cs), en camelCase y con enums como texto.

export type EstadoFactura = 'Borrador' | 'Emitida' | 'Anulada';
export type ResultadoAuditoria = 'Exito' | 'Denegado' | 'Fallido';

export interface Pagina<T> {
  readonly items: readonly T[];
  readonly total: number;
  readonly numeroPagina: number;
  readonly tamano: number;
}

export interface FacturaResumen {
  readonly id: string;
  readonly clienteId: string;
  readonly clienteNombre: string;
  readonly numero: string | null;
  readonly estado: EstadoFactura;
  readonly fechaEmision: string | null;
  readonly creadaEn: string;
  readonly subtotal: number;
  readonly isv: number;
  readonly total: number;
}

export interface Linea {
  readonly id: number;
  readonly descripcion: string;
  readonly cantidad: number;
  readonly precioUnitario: number;
  readonly exento: boolean;
  readonly importe: number;
}

export interface FacturaDetalle extends FacturaResumen {
  readonly clienteRtn: string | null;
  readonly cai: string | null;
  readonly creadaPor: string;
  readonly motivoAnulacion: string | null;
  readonly lineas: readonly Linea[];
}

export interface Cliente {
  readonly id: string;
  readonly nombre: string;
  readonly rtn: string | null;
  readonly email: string | null;
}

export interface Obligado {
  readonly id: string;
  readonly rtn: string;
  readonly razonSocial: string;
  readonly cai: string;
  readonly prefijo: string;
  readonly rangoDesde: number;
  readonly rangoHasta: number;
  readonly siguienteCorrelativo: number;
  readonly fechaLimiteEmision: string;
  readonly activo: boolean;
}

export interface EventoAuditoria {
  readonly id: string;
  readonly timestamp: string;
  readonly origen: 'billing' | 'identity';
  readonly actor: string | null;
  readonly accion: string;
  readonly recurso: string | null;
  readonly resultado: ResultadoAuditoria;
  readonly ip: string | null;
  readonly correlationId: string | null;
  readonly detalle: string | null;
}

export interface NuevaLinea {
  descripcion: string;
  cantidad: number;
  precioUnitario: number;
  exento: boolean;
}

export interface DatosCliente {
  nombre: string;
  rtn: string | null;
  email: string | null;
}

export interface ActualizarCai {
  cai: string;
  prefijo: string;
  rangoDesde: number;
  rangoHasta: number;
  fechaLimiteEmision: string;
}
