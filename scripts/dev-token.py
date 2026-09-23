#!/usr/bin/env python3
"""JWT HS256 exclusivo de Development para probar Billing aislado con `dotnet run` (el compose no usa este modo)."""
import argparse
import base64
import hashlib
import hmac
import json
import os
import time
import uuid


def b64(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--tenant", required=True, type=uuid.UUID)
parser.add_argument("--role", choices=["Admin", "Facturador", "Auditor", "Cliente"], required=True)
parser.add_argument("--cliente", type=uuid.UUID)
parser.add_argument("--user", type=uuid.UUID, default=uuid.uuid4())
args = parser.parse_args()
key = os.environ.get("BILLING_TEST_JWT_KEY", "")
if len(key.encode()) < 32:
    parser.error("Defina BILLING_TEST_JWT_KEY (mínimo 32 bytes), la misma clave que Billing__TestJwtKey de dotnet run")
if args.role == "Cliente" and args.cliente is None:
    parser.error("El rol Cliente requiere --cliente")

header = {"alg": "HS256", "typ": "JWT"}
payload = {
    "iss": "secunitec-test", "aud": "secunitec-billing", "sub": str(args.user),
    "tenant_id": str(args.tenant), "role": args.role,
    "iat": int(time.time()), "exp": int(time.time()) + 900,
}
if args.cliente is not None:
    payload["cliente_id"] = str(args.cliente)
parts = [b64(json.dumps(x, separators=(",", ":")).encode()) for x in (header, payload)]
message = ".".join(parts).encode()
print(message.decode() + "." + b64(hmac.new(key.encode(), message, hashlib.sha256).digest()))
