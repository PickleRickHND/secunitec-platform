import { use } from 'react';
import { AuthContext, type Sesion } from './authContext';

export function useSesion(): Sesion {
  const sesion = use(AuthContext);
  if (!sesion) {
    throw new Error('useSesion debe usarse dentro de AuthProvider.');
  }

  return sesion;
}
