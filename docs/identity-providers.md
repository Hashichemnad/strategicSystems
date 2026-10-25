# Identity providers: customer configuration guide

Everything here is done in the application, at **Administration → Identity providers**, by a system administrator.
No configuration files are edited and nothing is restarted. A new provider is **disabled** until you enable it, so you
can save, test and correct it first.

Local accounts (user name + password + two-step verification) always keep working beside the providers.

## How it works

| Topic | Behaviour |
|---|---|
| Who may sign in | Only people who are **employees** in *Organisation → Employees*. A provider never creates an employee. |
| Linking | At the first sign-in the external account is linked to an employee by the field chosen in **Match employees by** (email address, employee number or user name), read from the claim / attribute names in **Claims and attributes**. Only an **active** employee is linked. |
| Link automatically | On (default): the first sign-in links the account. Off: an administrator links accounts beforehand. |
| Create the account on first sign-in | Off (default): the employee must already have an account (*Administration → Users*). On: an account is created for an existing employee without one. |
| Two-step verification after SSO | *Administration → Security → Also ask for two-step verification after single sign-on* (off by default; the provider's own MFA normally applies). |
| Secrets | Client secrets and service-account passwords are encrypted with Data Protection. The screen never shows them again; leave the field empty to keep the stored value. |
| Test | **Test the connection** reads the discovery document / metadata, or binds to the directory. The result is shown on the provider card. |
| Logs | Every sign-in, failure, link and provider change is written to the authentication event log (and the server log). |

The addresses to register at the identity provider are shown in the editor after the first save
(**Register these addresses at the identity provider**). They are built from **Administration → Email server →
Application address**, which must be set to the public HTTPS address users open (for example
`https://strategy.example.org`).

| Type | Address to register | Pattern |
|---|---|---|
| OpenID Connect / UAE Pass | Redirect (callback) | `https://<app>/signin-ext/<code>` |
| SAML 2.0 | Assertion Consumer Service (POST) | `https://<app>/saml2/<code>/Acs` |
| SAML 2.0 | Service provider metadata (download) | `https://<app>/saml2/<code>` |

`<code>` is the provider **Code** you choose (lower case, for example `entra`). It cannot be changed later because it
is part of these addresses.

---

## Microsoft Entra ID (Azure AD), OpenID Connect

1. In the Entra admin centre: **App registrations → New registration**.
   - Redirect URI: platform **Web**, `https://<app>/signin-ext/entra`.
   - **Certificates & secrets → New client secret**. Copy the value.
   - **Token configuration → Add optional claim → ID token**: `email`, `preferred_username`. To link by employee
     number, add `employeeid` (directory extension) as an optional claim.
2. In the application: **New provider**.

   | Field | Value |
   |---|---|
   | Type | OpenID Connect |
   | Code | `entra` |
   | Button text (Arabic / English) | for example `الدخول بحساب المؤسسة` / `Sign in with the organisation account` |
   | Client secret | the secret value |
   | Authority (issuer) URL | `https://login.microsoftonline.com/<tenant-id>/v2.0` |
   | Client ID | the Application (client) ID |
   | Scopes | `openid profile email` |
   | Claims | Subject `sub` (or `oid`), Email `email`, User name `preferred_username`, Employee number `employeeid` if used |
   | Match employees by | Email address (or Employee number) |

3. **Save → Test the connection** (expects: "The discovery document was read") → **Enable**.

## ADFS (Windows Server 2016+), OpenID Connect

1. In ADFS Management: **Application Groups → Add → Server application accessing a web API** (or *Web browser
   accessing a web application* with a confidential client).
   - Redirect URI: `https://<app>/signin-ext/adfs`.
   - Generate a shared secret.
   - Issuance transform rules: send `E-Mail-Addresses` as `email`, `SAM-Account-Name` as `preferred_username`, and the
     employee ID attribute (for example `employeeID`) as `employee_no` if you link by number.
2. In the application: type **OpenID Connect**, code `adfs`.
   - **Authority**: `https://<adfs-host>/adfs`.
   - Client ID and secret from step 1.
   - Response mode `query`.
   - Claims as mapped above.
3. Save, test, enable.

## Keycloak (or any standards-compliant OIDC provider)

- Client: **OpenID Connect**, *Client authentication* on, *Standard flow* on, valid redirect URI
  `https://<app>/signin-ext/keycloak`.
- Authority: `https://<keycloak>/realms/<realm>`.
- The automated tests run exactly this against a Keycloak 26 container
  (`tests/Strategy.IntegrationTests/External`, realm file `strategy-test-realm.json`).

## UAE Pass

UAE Pass is an OpenID Connect profile with fixed endpoints. Choose type **UAE Pass**; the endpoints are filled in.

1. **Onboarding** (through TDRA / the UAE Pass partner portal): register the service provider, request **staging**
   credentials and give the redirect URI `https://<app>/signin-ext/uaepass`.
2. In the application:

   | Field | Value |
   |---|---|
   | Environment | **Staging** first (`stg-id.uaepass.ae`), then **Production** (`id.uaepass.ae`) after UAE Pass approval |
   | Client ID / Client secret | from the onboarding |
   | Scopes | `urn:uae:digitalid:profile:general` (default) |
   | Authentication level (acr_values) | `urn:safelayer:tws:policies:authentication:level:low` (default; use the level agreed with UAE Pass) |
   | Allowed user types | `SOP2,SOP3` (default: verified accounts only). Add `SOP1` only if basic accounts are acceptable |
   | Claims | Subject `sub`, Email `email`, User type `userType`. To link by Emirates ID / mobile, map the agreed claim to *Employee number* or *User name* and store the same value on the employee |
   | Match employees by | Email address (default) or the field you mapped |

   *Endpoint overrides* are only for exceptional cases; normally leave them empty.
3. Save, **Test the connection** (expects: "The authorization endpoint answered"), enable, and run the UAE Pass
   staging test cases. Switch **Environment** to Production with the production credentials when approved.

People whose UAE Pass account type is not allowed see "This type of UAE Pass account cannot sign in here."

## SAML 2.0 (ADFS relying party, Shibboleth, any SAML IdP)

1. In the application: **New provider**, type **SAML 2.0**, code for example `saml`. Then fill in:
   - **Identity provider entity ID**;
   - either the **Metadata URL** of the IdP (preferred: certificates roll over automatically), or the **Sign-in (SSO) URL** plus the **Identity provider signing certificate (PEM)**.

   Save.
2. Download the **service provider metadata** from `https://<app>/saml2/saml` (shown in the editor) and import it at
   the IdP. Or enter manually:
   - entity ID = **Our entity ID** (default `https://<app>/saml2/saml`);
   - ACS = `https://<app>/saml2/saml/Acs` (HTTP-POST).

   Our authentication requests are signed. The signing certificate is generated per provider and included in the metadata.
3. Release attributes:
   - **NameID** (persistent) as the subject;
   - `email` (or the claim URI you enter under *Claims*);
   - optionally an employee number attribute.
4. Test (expects: "The metadata was read and the entity ID matches"), then enable.

**Accept IdP-initiated sign-in** stays off unless the IdP portal must start sign-ins. The tests use Keycloak as a SAML IdP.

## LDAP / Active Directory

LDAP accounts sign in on the **normal sign-in form** (no extra button). The application first looks the user up with a
service account, then checks the password by binding as that user.

| Field | Value (Active Directory example) |
|---|---|
| Server / Port | `dc01.example.local` / `636` |
| Connection security | **LDAPS (TLS)** (default). StartTLS on 389 also works. *None* only on isolated test networks |
| Service account DN / password | a read-only account, for example `CN=svc-strategy,OU=Service,DC=example,DC=local` |
| Search base DN | `OU=Staff,DC=example,DC=local` |
| User filter | `(&(objectClass=person)(sAMAccountName={0}))`. The default `(&(objectClass=person)(\|(uid={0})(sAMAccountName={0})))` fits AD and OpenLDAP; `{0}` is the escaped user name |
| Attributes | Email `mail`, Employee number `employeeNumber` (AD often uses `employeeID`), User name `sAMAccountName` (AD) / `uid` (OpenLDAP) |

**Test the connection** with a sample user name: it binds with the service account and searches for that user.
The domain controllers' CA must be trusted by the server. In the container, mount the CA into
`/usr/local/share/ca-certificates/` and rebuild, or use the organisation's base image. The tests run against an
OpenLDAP container (`osixia/openldap`, seed file `people.ldif`).

## Troubleshooting

| Message at sign-in | Meaning |
|---|---|
| Your external account is not linked to an employee | No **active** employee matched the chosen field. Check the claim mapping and the employee record. |
| You do not have an account in this system yet | Matched an employee without an account, and *Create the account on first sign-in* is off. Create it in *Users*. |
| Your account is deactivated | The account or employee is inactive. |
| Sign-in with the external provider did not complete | The IdP returned an error or the callback failed. The server log (with the correlation ID) has the reason. |

The provider card shows the last test result. *Disable* removes the sign-in button immediately; linked accounts are kept.
