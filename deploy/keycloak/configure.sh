#!/usr/bin/env bash
set -euo pipefail

KCADM=/opt/keycloak/bin/kcadm.sh
SERVER="${KEYCLOAK_INTERNAL_URL:-http://keycloak:8080}"
ADMIN_USER="${KC_BOOTSTRAP_ADMIN_USERNAME:-admin}"
ADMIN_PASSWORD="${KC_BOOTSTRAP_ADMIN_PASSWORD:-deleguard-dev-keycloak}"
PLATFORM_PASSWORD="${PLATFORM_ADMIN_PASSWORD:-DeleguardPlatform!23}"
CONSOLE_ORIGIN="${DELEGUARD_PUBLIC_ORIGIN:-http://localhost:8082}"
KEYCLOAK_PUBLIC="${KEYCLOAK_PUBLIC_ORIGIN:-http://localhost:8081}"

until "$KCADM" config credentials --server "$SERVER" --realm master --user "$ADMIN_USER" --password "$ADMIN_PASSWORD" >/dev/null 2>&1; do
  sleep 2
done

"$KCADM" update realms/deleguard \
  -s enabled=true \
  -s displayName=Deleguard \
  -s loginTheme=deleguard \
  -s accountTheme=keycloak.v3 \
  -s emailTheme=keycloak \
  -s internationalizationEnabled=false \
  -s registrationAllowed=true \
  -s registrationEmailAsUsername=true \
  -s loginWithEmailAllowed=true \
  -s duplicateEmailsAllowed=false \
  -s resetPasswordAllowed=true \
  -s rememberMe=true \
  -s verifyEmail=false \
  -s editUsernameAllowed=false \
  -s bruteForceProtected=true \
  -s permanentLockout=false \
  -s sslRequired=none \
  -s ssoSessionIdleTimeout=1800 \
  -s ssoSessionMaxLifespan=36000 \
  -s accessTokenLifespan=300 \
  -s accessCodeLifespanLogin=1800 \
  -s actionTokenGeneratedByUserLifespan=86400 \
  -s eventsEnabled=true \
  -s adminEventsEnabled=true \
  -s adminEventsDetailsEnabled=true \
  -s 'smtpServer={"host":"mailpit","port":"1025","from":"noreply@deleguard.local","fromDisplayName":"Deleguard","replyTo":"noreply@deleguard.local","auth":"false","ssl":"false","starttls":"false"}' \
  -s "attributes.frontendUrl=${KEYCLOAK_PUBLIC}" \
  -s 'attributes.userProfileEnabled=true' \
  || true

if [ -f /opt/keycloak/user-profile.json ]; then
  "$KCADM" update users/profile -r deleguard -f /opt/keycloak/user-profile.json || true
fi

set_required_action() {
  local alias="$1"
  shift
  "$KCADM" update "authentication/required-actions/${alias}" -r deleguard "$@" >/dev/null 2>&1 || true
}

# Keep actions available in Admin, but never block first login on lastName / profile completeness.
set_required_action UPDATE_PASSWORD -s enabled=true -s defaultAction=false
set_required_action CONFIGURE_TOTP -s enabled=true -s defaultAction=false
set_required_action VERIFY_EMAIL -s enabled=true -s defaultAction=false
set_required_action UPDATE_PROFILE -s enabled=true -s defaultAction=false
set_required_action VERIFY_PROFILE -s enabled=false -s defaultAction=false
set_required_action TERMS_AND_CONDITIONS -s enabled=false -s defaultAction=false
set_required_action UPDATE_USER_LOCALE -s enabled=false -s defaultAction=false
set_required_action delete_account -s enabled=false -s defaultAction=false
set_required_action UPDATE_EMAIL -s enabled=true -s defaultAction=false
set_required_action CONFIGURE_RECOVERY_AUTHN_CODES -s enabled=true -s defaultAction=false
set_required_action webauthn-register -s enabled=true -s defaultAction=false
set_required_action webauthn-register-passwordless -s enabled=true -s defaultAction=false

disable_flow_execution() {
  local flow="$1"
  local needle="$2"
  local raw block lowered id
  raw="$("$KCADM" get "authentication/flows/${flow}/executions" -r deleguard 2>/dev/null || true)"
  [ -n "$raw" ] || return 0
  needle="$(printf '%s' "$needle" | tr '[:upper:]' '[:lower:]')"
  while IFS= read -r block; do
    [ -n "$block" ] || continue
    lowered="$(printf '%s' "$block" | tr '[:upper:]' '[:lower:]')"
    case "$lowered" in
      *"$needle"*)
        id="$(printf '%s' "$block" | tr ',' '\n' | sed -n 's/.*"id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)"
        [ -n "$id" ] || continue
        "$KCADM" update "authentication/flows/${flow}/executions" -r deleguard \
          -b "{\"id\":\"${id}\",\"requirement\":\"DISABLED\"}" >/dev/null 2>&1 || true
        ;;
    esac
  done <<EOF
$(printf '%s' "$raw" | tr '{' '\n')
EOF
}

disable_flow_execution "first broker login" "review profile"
disable_flow_execution "first broker login" "idp-review-profile"
disable_flow_execution "registration" "review profile"

if ! "$KCADM" get roles/platform-admin -r deleguard >/dev/null 2>&1; then
  "$KCADM" create roles -r deleguard -s name=platform-admin -s 'description=Deleguard platform operator'
fi

CLIENT_UUID="$("$KCADM" get clients -r deleguard -q clientId=deleguard-console --fields id --format csv --noquotes 2>/dev/null | tail -n 1 | tr -d '\r')"
if [ -z "$CLIENT_UUID" ] || [ "$CLIENT_UUID" = "id" ]; then
  "$KCADM" create clients -r deleguard \
    -s clientId=deleguard-console \
    -s name='Deleguard console' \
    -s enabled=true \
    -s protocol=openid-connect \
    -s publicClient=false \
    -s clientAuthenticatorType=client-secret \
    -s secret=deleguard-console-dev-secret-change-me \
    -s standardFlowEnabled=true \
    -s implicitFlowEnabled=false \
    -s directAccessGrantsEnabled=false \
    -s serviceAccountsEnabled=false
  CLIENT_UUID="$("$KCADM" get clients -r deleguard -q clientId=deleguard-console --fields id --format csv --noquotes | tail -n 1 | tr -d '\r')"
fi

if [ -n "$CLIENT_UUID" ] && [ "$CLIENT_UUID" != "id" ]; then
  "$KCADM" update "clients/${CLIENT_UUID}" -r deleguard \
    -s "rootUrl=${CONSOLE_ORIGIN}" \
    -s 'baseUrl=/' \
    -s "adminUrl=${CONSOLE_ORIGIN}" \
    -s enabled=true \
    -s publicClient=false \
    -s protocol=openid-connect \
    -s clientAuthenticatorType=client-secret \
    -s standardFlowEnabled=true \
    -s implicitFlowEnabled=false \
    -s directAccessGrantsEnabled=false \
    -s serviceAccountsEnabled=false \
    -s fullScopeAllowed=true \
    -s frontchannelLogout=true \
    -s 'redirectUris=["'"${CONSOLE_ORIGIN}"'/*","http://127.0.0.1:8082/*","'"${CONSOLE_ORIGIN}"'/signin-oidc","http://127.0.0.1:8082/signin-oidc","'"${CONSOLE_ORIGIN}"'/signout-callback-oidc","http://127.0.0.1:8082/signout-callback-oidc"]' \
    -s 'webOrigins=["'"${CONSOLE_ORIGIN}"'","http://127.0.0.1:8082","+"]' \
    -s 'attributes."pkce.code.challenge.method"=S256' \
    -s "attributes.\"post.logout.redirect.uris\"=+##${CONSOLE_ORIGIN}/*##http://127.0.0.1:8082/*" \
    -s 'attributes."require.pushed.authorization.requests"=false' \
    -s 'attributes."oauth2.device.authorization.grant.enabled"=false' \
    -s 'attributes."oidc.ciba.grant.enabled"=false' \
    -s 'attributes."backchannel.logout.session.required"=true' \
    -s 'attributes."use.refresh.tokens"=true' \
    -s 'attributes."client.use.lightweight.access.token.enabled"=false' \
    || true

  ensure_mapper() {
    local name="$1"
    shift
    if "$KCADM" get "clients/${CLIENT_UUID}/protocol-mappers/models" -r deleguard | grep -q "\"name\" : \"${name}\""; then
      return 0
    fi
    "$KCADM" create "clients/${CLIENT_UUID}/protocol-mappers/models" -r deleguard -s "name=${name}" "$@" || true
  }

  ensure_mapper company \
    -s protocol=openid-connect -s protocolMapper=oidc-usermodel-attribute-mapper \
    -s 'config."user.attribute"=company' -s 'config."claim.name"=company' \
    -s 'config."id.token.claim"=true' -s 'config."access.token.claim"=true' -s 'config."userinfo.token.claim"=true' \
    -s 'config."jsonType.label"=String'

  ensure_mapper realm-roles \
    -s protocol=openid-connect -s protocolMapper=oidc-usermodel-realm-role-mapper \
    -s 'config."claim.name"=roles' -s 'config.multivalued=true' \
    -s 'config."id.token.claim"=true' -s 'config."access.token.claim"=true' -s 'config."userinfo.token.claim"=true' \
    -s 'config."jsonType.label"=String'

  ensure_mapper email \
    -s protocol=openid-connect -s protocolMapper=oidc-usermodel-property-mapper \
    -s 'config."user.attribute"=email' -s 'config."claim.name"=email' \
    -s 'config."id.token.claim"=true' -s 'config."access.token.claim"=true' -s 'config."userinfo.token.claim"=true' \
    -s 'config."jsonType.label"=String'

  ensure_mapper email-verified \
    -s protocol=openid-connect -s protocolMapper=oidc-usermodel-property-mapper \
    -s 'config."user.attribute"=emailVerified' -s 'config."claim.name"=email_verified' \
    -s 'config."id.token.claim"=true' -s 'config."access.token.claim"=true' -s 'config."userinfo.token.claim"=true' \
    -s 'config."jsonType.label"=boolean'

  ensure_mapper given-name \
    -s protocol=openid-connect -s protocolMapper=oidc-usermodel-property-mapper \
    -s 'config."user.attribute"=firstName' -s 'config."claim.name"=given_name' \
    -s 'config."id.token.claim"=true' -s 'config."access.token.claim"=true' -s 'config."userinfo.token.claim"=true' \
    -s 'config."jsonType.label"=String'

  ensure_mapper username \
    -s protocol=openid-connect -s protocolMapper=oidc-usermodel-property-mapper \
    -s 'config."user.attribute"=username' -s 'config."claim.name"=preferred_username' \
    -s 'config."id.token.claim"=true' -s 'config."access.token.claim"=true' -s 'config."userinfo.token.claim"=true' \
    -s 'config."jsonType.label"=String'
fi

ACCOUNT_UUID="$("$KCADM" get clients -r deleguard -q clientId=account-console --fields id --format csv --noquotes 2>/dev/null | tail -n 1 | tr -d '\r')"
if [ -n "$ACCOUNT_UUID" ] && [ "$ACCOUNT_UUID" != "id" ]; then
  "$KCADM" update "clients/${ACCOUNT_UUID}" -r deleguard \
    -s "rootUrl=${KEYCLOAK_PUBLIC}/realms/deleguard/account/" \
    -s "baseUrl=/" \
    -s "redirectUris=[\"${KEYCLOAK_PUBLIC}/realms/deleguard/account/*\"]" \
    -s "webOrigins=[\"${KEYCLOAK_PUBLIC}\"]" \
    || true
fi

ensure_user() {
  local realm="$1"
  local username="$2"
  local email="$3"
  local first="$4"
  local last="$5"
  local password="$6"
  local role="$7"
  if ! "$KCADM" get users -r "$realm" -q "username=${username}" --fields username | grep -q "$username"; then
    "$KCADM" create users -r "$realm" \
      -s "username=${username}" \
      -s "email=${email}" \
      -s enabled=true \
      -s emailVerified=true \
      -s "firstName=${first}" \
      -s "lastName=${last}" \
      -s 'requiredActions=[]'
    "$KCADM" set-password -r "$realm" --username "$username" --new-password "$password"
  fi
  local user_id
  user_id="$("$KCADM" get users -r "$realm" -q "username=${username}" --fields id --format csv --noquotes 2>/dev/null | tail -n 1 | tr -d '\r')"
  if [ -n "$user_id" ] && [ "$user_id" != "id" ]; then
    "$KCADM" update "users/${user_id}" -r "$realm" \
      -s enabled=true \
      -s emailVerified=true \
      -s "email=${email}" \
      -s "firstName=${first}" \
      -s "lastName=${last}" \
      -s 'requiredActions=[]' || true
  fi
  if [ -n "$role" ]; then
    "$KCADM" add-roles -r "$realm" --uusername "$username" --rolename "$role" || true
  fi
}

ensure_user deleguard platform-admin@deleguard.local platform-admin@deleguard.local Platform Admin "$PLATFORM_PASSWORD" platform-admin
ensure_user master platform-admin platform-admin@deleguard.local Platform Admin "$PLATFORM_PASSWORD" admin

upsert_idp() {
  local alias="$1"
  local provider="$2"
  local display="$3"
  local client_id="${4:-}"
  local client_secret="${5:-}"
  shift 5 || true
  local enabled="false"
  local hide="true"
  if [ -n "$client_id" ] && [ -n "$client_secret" ]; then
    enabled="true"
    hide="false"
  else
    client_id="${client_id:-placeholder-not-configured}"
    client_secret="${client_secret:-placeholder-not-configured}"
  fi
  if "$KCADM" get "identity-provider/instances/${alias}" -r deleguard >/dev/null 2>&1; then
    "$KCADM" update "identity-provider/instances/${alias}" -r deleguard \
      -s "enabled=${enabled}" -s trustEmail=true -s "displayName=${display}" \
      -s "hideOnLogin=${hide}" -s firstBrokerLoginFlowAlias='first broker login' \
      -s "config.clientId=${client_id}" -s "config.clientSecret=${client_secret}" \
      -s 'config.defaultScope=openid profile email' -s 'config.syncMode=IMPORT' \
      -s 'config.guiOrder=1' "$@" || true
  else
    "$KCADM" create identity-provider/instances -r deleguard \
      -s "alias=${alias}" -s "providerId=${provider}" -s "enabled=${enabled}" -s trustEmail=true \
      -s "displayName=${display}" -s "hideOnLogin=${hide}" \
      -s firstBrokerLoginFlowAlias='first broker login' \
      -s "config.clientId=${client_id}" -s "config.clientSecret=${client_secret}" \
      -s 'config.defaultScope=openid profile email' -s 'config.syncMode=IMPORT' \
      -s 'config.guiOrder=1' "$@" || true
  fi
}

upsert_idp google google Google "${GOOGLE_CLIENT_ID:-}" "${GOOGLE_CLIENT_SECRET:-}"
upsert_idp microsoft microsoft Microsoft "${MICROSOFT_CLIENT_ID:-}" "${MICROSOFT_CLIENT_SECRET:-}" \
  -s "config.tenant=${MICROSOFT_TENANT_ID:-common}"

echo "deleguard keycloak configure complete"
