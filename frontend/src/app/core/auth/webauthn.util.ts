/**
 * Minimal WebAuthn helpers — convert between base64url strings (what the server
 * sends/receives in JSON) and the ArrayBuffers that navigator.credentials uses.
 */

export function base64urlToBuffer(b64u: string): ArrayBuffer {
  const b64 = b64u.replace(/-/g, '+').replace(/_/g, '/').padEnd(b64u.length + (4 - (b64u.length % 4)) % 4, '=');
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out.buffer;
}

export function bufferToBase64url(buf: ArrayBuffer): string {
  const bytes = new Uint8Array(buf);
  let bin = '';
  for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

interface CreationOptions {
  challenge: string;
  user: { id: string; name: string; displayName: string };
  rp: { id?: string; name: string };
  pubKeyCredParams: PublicKeyCredentialParameters[];
  excludeCredentials?: { id: string; type: string; transports?: AuthenticatorTransport[] }[];
  authenticatorSelection?: AuthenticatorSelectionCriteria;
  timeout?: number;
  attestation?: AttestationConveyancePreference;
}

export function decodeCreationOptions(json: CreationOptions): PublicKeyCredentialCreationOptions {
  return {
    ...json,
    challenge: base64urlToBuffer(json.challenge),
    user: { ...json.user, id: base64urlToBuffer(json.user.id) },
    excludeCredentials: (json.excludeCredentials ?? []).map((c) => ({
      ...c, id: base64urlToBuffer(c.id), type: c.type as PublicKeyCredentialType,
    })),
  };
}

interface RequestOptions {
  challenge: string;
  rpId?: string;
  timeout?: number;
  userVerification?: UserVerificationRequirement;
  allowCredentials?: { id: string; type: string; transports?: AuthenticatorTransport[] }[];
}

export function decodeRequestOptions(json: RequestOptions): PublicKeyCredentialRequestOptions {
  return {
    ...json,
    challenge: base64urlToBuffer(json.challenge),
    allowCredentials: (json.allowCredentials ?? []).map((c) => ({
      ...c, id: base64urlToBuffer(c.id), type: c.type as PublicKeyCredentialType,
    })),
  };
}

export function encodeAttestationResponse(cred: PublicKeyCredential): unknown {
  const att = cred.response as AuthenticatorAttestationResponse;
  return {
    id: cred.id,
    rawId: bufferToBase64url(cred.rawId),
    type: cred.type,
    extensions: cred.getClientExtensionResults(),
    response: {
      attestationObject: bufferToBase64url(att.attestationObject),
      clientDataJSON:    bufferToBase64url(att.clientDataJSON),
    },
  };
}

export function encodeAssertionResponse(cred: PublicKeyCredential): unknown {
  const ass = cred.response as AuthenticatorAssertionResponse;
  return {
    id: cred.id,
    rawId: bufferToBase64url(cred.rawId),
    type: cred.type,
    extensions: cred.getClientExtensionResults(),
    response: {
      authenticatorData: bufferToBase64url(ass.authenticatorData),
      clientDataJSON:    bufferToBase64url(ass.clientDataJSON),
      signature:         bufferToBase64url(ass.signature),
      userHandle:        ass.userHandle ? bufferToBase64url(ass.userHandle) : null,
    },
  };
}
