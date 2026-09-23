const audit = db.getSiblingDB('secunitec_audit');
audit.createUser({
  user: 'billing_audit',
  pwd: process.env.MONGO_BILLING_PASSWORD,
  roles: [{ role: 'readWrite', db: 'secunitec_audit' }]
});
audit.eventos.createIndex({ tenant_id: 1, timestamp: -1 });
