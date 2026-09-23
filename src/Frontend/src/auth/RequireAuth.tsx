import { type ReactNode, useEffect } from 'react';
import { NoAutorizadoPage } from '../features/publico/NoAutorizadoPage';
import { type Permiso, puede } from './roles';
import { useSesion } from './useSesion';

/** Rutas protegidas: sin sesión inicia el login; sin el permiso muestra una explicación (el servidor igual decide). */
export function RequireAuth({ permiso, children }: { readonly permiso?: Permiso; readonly children: ReactNode }) {
  const { user, cargando, roles, iniciarSesion } = useSesion();

  useEffect(() => {
    if (!cargando && !user) {
      void iniciarSesion();
    }
  }, [cargando, user, iniciarSesion]);

  if (cargando || !user) {
    return (
      <p className="muted" role="status">
        Verificando la sesión…
      </p>
    );
  }

  if (permiso && !puede(roles, permiso)) {
    return <NoAutorizadoPage />;
  }

  return children;
}
