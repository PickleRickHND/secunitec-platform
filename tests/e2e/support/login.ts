import { expect, type Page } from '@playwright/test';

/**
 * Login OIDC real: el SPA redirige a la página de Identity (a través del gateway), se envía el formulario y el SPA
 * canjea el código con PKCE. Si el gateway limitó /connect/token (5 por minuto por IP), se espera la cuenta regresiva
 * de la pantalla y se reintenta, en vez de fallar o de bajar el límite.
 */
export async function iniciarSesion(page: Page, credenciales: { email: string; password: string }): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Iniciar sesión' }).click();
  await expect(page).toHaveURL(/\/account\/login/);
  await page.getByLabel('Correo').fill(credenciales.email);
  await page.getByLabel('Contraseña').fill(credenciales.password);
  await page.getByRole('button', { name: 'Iniciar sesión' }).click();

  const inicio = page.getByRole('heading', { name: 'Inicio', level: 1 });
  const limitado = page.getByRole('heading', { name: 'Demasiados inicios de sesión seguidos' });
  await expect(inicio.or(limitado)).toBeVisible({ timeout: 30_000 });
  if (await limitado.isVisible()) {
    const reintentar = page.getByRole('button', { name: 'Reintentar' });
    await expect(reintentar).toBeEnabled({ timeout: 70_000 });
    await reintentar.click();
    await expect(inicio).toBeVisible({ timeout: 30_000 });
  }
}
