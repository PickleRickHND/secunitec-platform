import type { User } from 'oidc-client-ts';
import { type ReactNode, useCallback, useEffect, useMemo, useState } from 'react';
import { configureHttp } from '../api/http';
import { userManager } from './oidc';
import { AuthContext, type Sesion } from './authContext';
import { normalizeRoles } from './roles';

const rutaActual = () => window.location.pathname + window.location.search;

export function AuthProvider({ children }: { readonly children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [cargando, setCargando] = useState(true);

  const iniciarSesion = useCallback(async (volverA: string = rutaActual()) => {
    await userManager.signinRedirect({ state: { volverA } });
  }, []);

  const accessToken = useCallback(async () => {
    const actual = await userManager.getUser();
    if (actual && !actual.expired) {
      return actual.access_token;
    }

    if (actual?.refresh_token) {
      try {
        return (await userManager.signinSilent())?.access_token ?? null;
      } catch {
        return null;
      }
    }

    return null;
  }, []);

  useEffect(() => {
    let vigente = true;
    void userManager.getUser().then((actual) => {
      if (vigente) {
        setUser(actual && !actual.expired ? actual : actual?.refresh_token ? actual : null);
        setCargando(false);
      }
    });

    const cargado = (nuevo: User) => {
      setUser(nuevo);
    };
    const descargado = () => {
      setUser(null);
    };
    // Refresh token rechazado (Identity reiniciado o sesión revocada): volver a iniciar sesión.
    const renovacionFallida = () => {
      void userManager.removeUser().then(() => iniciarSesion());
    };
    userManager.events.addUserLoaded(cargado);
    userManager.events.addUserUnloaded(descargado);
    userManager.events.addSilentRenewError(renovacionFallida);

    configureHttp({
      tokenProvider: accessToken,
      onUnauthorized: () => {
        void userManager.removeUser().then(() => iniciarSesion());
      },
    });

    return () => {
      vigente = false;
      userManager.events.removeUserLoaded(cargado);
      userManager.events.removeUserUnloaded(descargado);
      userManager.events.removeSilentRenewError(renovacionFallida);
    };
  }, [accessToken, iniciarSesion]);

  const sesion = useMemo<Sesion>(() => {
    const profile = user?.profile;
    return {
      user,
      cargando,
      roles: normalizeRoles(profile?.role),
      email: typeof profile?.email === 'string' ? profile.email : null,
      clienteId: typeof profile?.cliente_id === 'string' ? profile.cliente_id : null,
      iniciarSesion,
      cerrarSesion: () => userManager.signoutRedirect(),
      accessToken,
    };
  }, [user, cargando, iniciarSesion, accessToken]);

  return <AuthContext value={sesion}>{children}</AuthContext>;
}
