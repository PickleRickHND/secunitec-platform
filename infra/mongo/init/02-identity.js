const audit = db.getSiblingDB('secunitec_audit');

audit.createUser({
  user: 'identity_audit',
  pwd: process.env.MONGO_IDENTITY_PASSWORD,
  roles: [{ role: 'readWrite', db: 'secunitec_audit' }]
});

audit.identity_events.createIndex({ action: 1, timestamp: -1 });
audit.identity_events.createIndex({ actor: 1, timestamp: -1 });
audit.identity_events.createIndex({ correlationId: 1, timestamp: -1 });
