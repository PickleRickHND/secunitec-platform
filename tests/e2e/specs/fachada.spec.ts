import { expect, test } from '@playwright/test';
import { usuarios } from '../support/entorno';
import { iniciarSesion } from '../support/login';

// Criterio de done de 4.1: login, crear factura, emitir, y el panel muestra 429 durante una ráfaga. Además los roles:
// el Auditor ve la auditoría y no escribe; el Cliente solo ve sus facturas.
test.describe.serial('Fachada (etapa 4.1)', () => {
  const clienteAjeno = `Cliente E2E ${Date.now()}`;

  test('Facturador: crea, agrega una línea, emite y anula una factura', async ({ page }) => {
    await iniciarSesion(page, usuarios.facturador);

    // Otro cliente con su factura: el usuario cliente@ no debe verla.
    await page.getByRole('link', { name: 'Clientes' }).click();
    await page.getByRole('button', { name: 'Nuevo cliente' }).click();
    await page.getByLabel('Nombre o razón social').fill(clienteAjeno);
    await page.getByRole('button', { name: 'Registrar cliente' }).click();
    await expect(page.getByRole('cell', { name: clienteAjeno })).toBeVisible();
    await page.goto('/facturas/nueva');
    await page.getByLabel('Cliente').selectOption({ label: clienteAjeno });
    await page.getByLabel('Descripción').fill('Servicio para otro cliente');
    await page.getByLabel('Precio unitario (L)').fill('10');
    await page.getByRole('button', { name: 'Guardar borrador' }).click();
    await expect(page.getByText('Sin número fiscal')).toBeVisible();

    // La factura del cliente de demostración: 100 gravado + 50 exento.
    await page.goto('/facturas/nueva');
    await page.getByLabel('Cliente').selectOption({ label: 'Cliente de demostración' });
    await page.getByLabel('Descripción').fill('Monitoreo mensual');
    await page.getByLabel('Precio unitario (L)').fill('100');
    await page.getByRole('button', { name: 'Agregar línea' }).click();
    await page.getByLabel('Descripción').nth(1).fill('Instalación');
    await page.getByLabel('Precio unitario (L)').nth(1).fill('50');
    await page.getByLabel('Exento de ISV').nth(1).check();
    await page.getByRole('button', { name: 'Guardar borrador' }).click();

    await expect(page.getByText('Borrador', { exact: true })).toBeVisible();
    const documento = page.getByRole('article');
    await expect(documento.getByText('L 165.00')).toBeVisible();

    // Una línea más desde el detalle: 2 × 25 gravado. El servidor recalcula: 200 + 22.50 de ISV.
    const agregar = page.getByRole('form', { name: 'Agregar línea' });
    await agregar.getByLabel('Descripción').fill('Visita técnica');
    await agregar.getByLabel('Cantidad').fill('2');
    await agregar.getByLabel('Precio unitario (L)').fill('25');
    await agregar.getByRole('button', { name: 'Agregar línea' }).click();
    await expect(documento.getByText('L 222.50')).toBeVisible();

    await page.getByRole('button', { name: 'Emitir factura' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Emitir factura' }).click();
    await expect(page.getByText('Emitida', { exact: true })).toBeVisible();
    await expect(page.locator('#numero-fiscal')).toHaveText(/^\d{3}-\d{3}-\d{2}-\d{8}$/);

    await page.getByRole('button', { name: 'Anular factura' }).click();
    await page.getByRole('dialog').getByLabel('Motivo de la anulación').fill('Prueba E2E: monto equivocado');
    await page.getByRole('dialog').getByRole('button', { name: 'Anular factura' }).click();
    await expect(page.getByText('Anulada', { exact: true })).toBeVisible();
    await expect(page.getByText('Motivo: Prueba E2E: monto equivocado')).toBeVisible();
  });

  test('Auditor: ve la auditoría y no tiene acciones de escritura', async ({ page }) => {
    await iniciarSesion(page, usuarios.auditor);

    await expect(page.getByRole('link', { name: 'Clientes' })).toHaveCount(0);
    await page.getByRole('link', { name: 'Auditoría' }).click();
    await expect(page.getByRole('cell', { name: 'Factura anulada' }).first()).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Inicio de sesión' }).first()).toBeVisible();
    await page.getByRole('link', { name: 'Facturas', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Facturas', level: 1 })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Nueva factura' })).toHaveCount(0);
  });

  test('Cliente: solo ve sus facturas', async ({ page }) => {
    await iniciarSesion(page, usuarios.cliente);

    await page.getByRole('link', { name: 'Facturas', exact: true }).click();
    await expect(page.getByRole('cell', { name: 'Cliente de demostración' }).first()).toBeVisible();
    await expect(page.getByRole('cell', { name: clienteAjeno })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Auditoría' })).toHaveCount(0);
    await page.goto('/auditoria');
    await expect(page.getByRole('heading', { name: 'Su rol no tiene acceso a esta sección' })).toBeVisible();
  });

  // Regresión: al borrar el usuario, RequireAuth pedía un login nuevo que le ganaba a /connect/endsession y, con la
  // cookie de Identity aún viva, la persona volvía a entrar sin darse cuenta.
  test('Cerrar sesión: termina también la sesión de Identity', async ({ page }) => {
    await iniciarSesion(page, usuarios.facturador);

    await page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await expect(page).toHaveURL('/');
    await expect(page.getByRole('button', { name: 'Iniciar sesión' })).toBeVisible();

    await page.goto('/inicio');
    await expect(page).toHaveURL(/\/account\/login/);
  });

  test('Panel de resiliencia: una ráfaga termina en 429 controlados y cero 5xx', async ({ page }) => {
    await iniciarSesion(page, usuarios.admin);
    await page.getByRole('link', { name: 'Resiliencia' }).click();

    await page.getByLabel('Con mi sesión').check();
    await page.getByLabel('Peticiones').fill('120');
    await page.getByLabel('En segundos').fill('10');
    await page.getByRole('button', { name: 'Lanzar ráfaga' }).click();

    await expect(page.getByText('120 de 120 respuestas')).toBeVisible({ timeout: 60_000 });
    const valor = async (etiqueta: RegExp) =>
      Number((await page.locator('p', { hasText: etiqueta }).locator('xpath=following-sibling::p[1]').first().innerText()).replace(/\D/g, ''));
    expect(await valor(/^429 bloqueadas/)).toBeGreaterThan(0);
    expect(await valor(/^200 aceptadas/)).toBeGreaterThan(0);
    expect(await valor(/^5xx \/ red/)).toBe(0);
    await expect(page.getByText('El gateway vuelve a aceptar sus peticiones en')).toBeVisible();
    await page.screenshot({ path: 'test-results/panel-resiliencia.png', fullPage: true });
  });
});
