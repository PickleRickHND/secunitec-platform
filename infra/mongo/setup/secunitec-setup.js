// R08 / A09 (etapa 2.1): usuarios, roles e índices de la auditoría en MongoDB.
// Lo ejecuta el servicio mongo-setup en cada `docker compose up`, con el usuario de arranque. Es idempotente: crea lo
// que falta y actualiza lo que existe, así también corrige volúmenes creados en etapas anteriores (antes los scripts
// de /docker-entrypoint-initdb.d solo corrían con el volumen vacío; ver docs/problemas-conocidos.md §5).
//
// La auditoría nunca se actualiza ni se borra desde la aplicación (docs/PLAN.md §3.1): los roles solo dan insert y
// find. Billing además lee los eventos de Identity de su tenant para la pantalla de auditoría.

const env = (name) => {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Falta la variable ${name}.`);
  }
  return value;
};

// MONGO_SETUP_HOST y MONGO_ADMIN_USER solo cambian en las pruebas (Testcontainers); en el compose valen lo de abajo.
const conn = new Mongo(`mongodb://${process.env.MONGO_SETUP_HOST || 'mongo'}:27017`);
const admin = conn.getDB('admin');
admin.auth(process.env.MONGO_ADMIN_USER || 'secunitec_bootstrap', env('MONGO_ADMIN_PASSWORD'));
const audit = conn.getDB('secunitec_audit');

function ensureRole(role, privileges) {
  if (audit.getRole(role)) {
    audit.updateRole(role, { privileges, roles: [] });
  } else {
    audit.createRole({ role, privileges, roles: [] });
  }
}

function ensureUser(user, pwd, role) {
  const roles = [{ role, db: 'secunitec_audit' }];
  if (audit.getUser(user)) {
    audit.updateUser(user, { pwd, roles });
  } else {
    audit.createUser({ user, pwd, roles });
  }
}

ensureRole('billingAudit', [
  { resource: { db: 'secunitec_audit', collection: 'eventos' }, actions: ['insert', 'find'] },
  { resource: { db: 'secunitec_audit', collection: 'identity_events' }, actions: ['find'] },
]);
ensureRole('identityAuditAppendOnly', [
  { resource: { db: 'secunitec_audit', collection: 'identity_events' }, actions: ['insert', 'find'] },
]);

// Antes de la etapa 4, billing_audit tenía readWrite (podía borrar eventos): updateUser reemplaza sus roles.
ensureUser('billing_audit', env('MONGO_BILLING_PASSWORD'), 'billingAudit');
ensureUser('identity_audit', env('MONGO_IDENTITY_PASSWORD'), 'identityAuditAppendOnly');

// createIndex crea la colección si falta (los usuarios de la aplicación no tienen createCollection) y es idempotente.
audit.eventos.createIndex({ tenant_id: 1, timestamp: -1 });
audit.eventos.createIndex({ actor: 1, timestamp: -1 });
audit.eventos.createIndex({ correlationId: 1, timestamp: -1 });
audit.identity_events.createIndex({ tenantId: 1, timestamp: -1 });
audit.identity_events.createIndex({ action: 1, timestamp: -1 });
audit.identity_events.createIndex({ actor: 1, timestamp: -1 });
audit.identity_events.createIndex({ correlationId: 1, timestamp: -1 });

print('mongo-setup: roles, usuarios e índices de secunitec_audit al día.');
