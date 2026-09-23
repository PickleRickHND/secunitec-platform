import type { User } from 'oidc-client-ts';
import { type ReactNode, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { configureHttp } from '../api/http';
import { userManager } from './oidc';
import { AuthContext, type Sesion } from './authContext';
import { normalizeRoles } from './roles';

const rutaActual = () => window.location.pathname + window.location.search;

export function AuthProvider({ children }: { readonly children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [cargando, setCargando] = useState(true);
  // signoutRedirect() borra el usuario antes de salir hacia /connect/endsession. Ese userUnloaded hacía que RequireAuth
  // pidiera un login nuevo cuya navegación a /connect/authorize le ganaba al cierre y, con la cookie de Identity aún
  // viva, la persona volvía a entrar sin darse cuenta. Mientras se cierra la sesión no se inicia otra.
  const saliendo = useRef(false);

  const iniciarSesion = useCallback(async (volverA: string = rutaActual()) => {
    if (saliendo.current) {
      return;
    }

    await userManager.signinRedirect({ state: { volverA } });
  }, []);

  const cerrarSesion = useCallback(async () => {
    saliendo.current = true;
    try {
      await userManager.signoutRedirect();
    } catch {
      // Sin respuesta de Identity los tokens locales igual quedaron borrados: se vuelve a la portada.
      saliendo.current = false;
      window.location.assign('/');
    }
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
      cerrarSesion,
      accessToken,
    };
  }, [user, cargando, iniciarSesion, cerrarSesion, accessToken]);

  return <AuthContext value={sesion}>{children}</AuthContext>;
}
