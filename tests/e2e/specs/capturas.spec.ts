import { expect, type Page, test } from '@playwright/test';
import { usuarios } from '../support/entorno';
import { iniciarSesion } from '../support/login';

// Revisión visual (no es parte del criterio de done): cada pantalla en escritorio, tableta y móvil. Solo corre con
// CAPTURAS=1. Carga cada pantalla una vez y cambia el viewport sin recargar, para no gastar el cupo del rate limiting.
const anchos = [
  { nombre: 'escritorio', width: 1440, height: 900 },
  { nombre: 'tableta', width: 768, height: 1024 },
  { nombre: 'movil', width: 375, height: 812 },
] as const;

async function capturar(page: Page, pantalla: string): Promise<void> {
  for (const ancho of anchos) {
    await page.setViewportSize({ width: ancho.width, height: ancho.height });
    await page.waitForTimeout(250);
    await page.screenshot({ path: `capturas/${pantalla}-${ancho.nombre}.png`, fullPage: true });
  }
}

test('capturas de todas las pantallas', async ({ page }) => {
  test.skip(process.env.CAPTURAS !== '1', 'Solo con CAPTURAS=1.');
  test.setTimeout(240_000);

  await page.goto('/');
  await capturar(page, '01-portada');
  await page.getByRole('button', { name: 'Iniciar sesión' }).click();
  await expect(page).toHaveURL(/\/account\/login/);
  await capturar(page, '02-login-identity');

  await page.setViewportSize({ width: 1440, height: 900 });
  await iniciarSesion(page, usuarios.admin);
  await capturar(page, '03-inicio');

  await page.getByRole('link', { name: 'Facturas', exact: true }).click();
  await expect(page.getByRole('table')).toBeVisible();
  await capturar(page, '04-facturas');

  await page.getByRole('link', { name: /^\d{3}-\d{3}-\d{2}-\d{8}$/ }).first().click();
  await expect(page.locator('#numero-fiscal')).toBeVisible();
  await capturar(page, '05-factura-detalle');

  await page.goto('/facturas/nueva');
  await expect(page.getByLabel('Cliente')).toBeVisible();
  await capturar(page, '06-factura-nueva');

  await page.getByRole('link', { name: 'Clientes' }).click();
  await expect(page.getByRole('table')).toBeVisible();
  await capturar(page, '07-clientes');

  await page.getByRole('link', { name: 'Obligado tributario' }).click();
  await expect(page.getByText('CAI vigente', { exact: true })).toBeVisible();
  await capturar(page, '08-obligado');

  await page.getByRole('link', { name: 'Auditoría' }).click();
  await expect(page.getByRole('table')).toBeVisible();
  await capturar(page, '09-auditoria');

  await page.getByRole('link', { name: 'Resiliencia' }).click();
  await page.getByLabel('Peticiones').fill('90');
  await page.getByLabel('En segundos').fill('6');
  await page.getByRole('button', { name: 'Lanzar ráfaga' }).click();
  await expect(page.getByText('90 de 90 respuestas')).toBeVisible({ timeout: 60_000 });
  await capturar(page, '10-resiliencia');
});
