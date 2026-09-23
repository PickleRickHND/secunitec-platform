// R09: OIDC Authorization Code + PKCE contra Identity a través del gateway (docs/PLAN.md §0 y §2.2).
// - Los tokens quedan en sessionStorage (por pestaña, se borran al cerrarla); la CSP estricta mitiga el XSS.
// - El state y el code_verifier del login también (etapa 5.1): por defecto oidc-client-ts los deja en localStorage,
//   donde sobreviven al cierre de la pestaña y los comparten todas las pestañas del origen.
// - La renovación usa el refresh token (offline_access) por fetch: sin iframes ni prompt=none.
import { UserManager, type UserManagerSettings, WebStorageStateStore } from 'oidc-client-ts';
import { config } from '../config';

export const SCOPES = 'openid profile email roles offline_access secunitec-billing';

/** Configuración del cliente OIDC; el almacenamiento se recibe para poder probarla sin navegador. */
export function configuracionOidc(almacen: Storage): UserManagerSettings {
  return {
    authority: config.gatewayUrl,
    client_id: 'spa-secunitec',
    redirect_uri: `${config.spaUrl}/auth/callback`,
    post_logout_redirect_uri: `${config.spaUrl}/auth/logout-callback`,
    response_type: 'code',
    scope: SCOPES,
    automaticSilentRenew: true,
    loadUserInfo: false,
    monitorSession: false,
    revokeTokensOnSignout: false,
    userStore: new WebStorageStateStore({ store: almacen }),
    stateStore: new WebStorageStateStore({ store: almacen }),
  };
}

export const userManager = new UserManager(configuracionOidc(window.sessionStorage));

/** Estado que viaja por el login para volver a la pantalla donde estaba la persona. */
export interface EstadoLogin {
  readonly volverA: string;
}

export function esEstadoLogin(value: unknown): value is EstadoLogin {
  return typeof value === 'object' && value !== null && typeof (value as EstadoLogin).volverA === 'string';
}
