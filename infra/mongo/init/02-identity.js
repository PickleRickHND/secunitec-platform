// R08 / A09: auditoría de Identity. El usuario de la aplicación solo puede insertar y leer eventos
// (docs/PLAN.md §3.1: la auditoría nunca se actualiza ni se borra desde la aplicación).
// Los scripts de /docker-entrypoint-initdb.d solo corren con un volumen vacío: ver docs/problemas-conocidos.md.
const audit = db.getSiblingDB('secunitec_audit');

audit.identity_events.createIndex({ action: 1, timestamp: -1 });
audit.identity_events.createIndex({ actor: 1, timestamp: -1 });
audit.identity_events.createIndex({ correlationId: 1, timestamp: -1 });

audit.createRole({
  role: 'identityAuditAppendOnly',
  privileges: [
    { resource: { db: 'secunitec_audit', collection: 'identity_events' }, actions: ['insert', 'find'] }
  ],
  roles: []
});

audit.createUser({
  user: 'identity_audit',
  pwd: process.env.MONGO_IDENTITY_PASSWORD,
  roles: [{ role: 'identityAuditAppendOnly', db: 'secunitec_audit' }]
});
