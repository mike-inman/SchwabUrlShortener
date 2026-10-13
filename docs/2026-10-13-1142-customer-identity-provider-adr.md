# ADR: one identity provider per deployment — 2026-10-13 11:42 CDT

Status: accepted for this implementation.

Each deployment selects either the customer External ID tenant or the existing workforce Entra tenant through configuration. The Web app has one OIDC scheme and the API has one bearer scheme. The customer tenant is the product target. Existing workforce configuration and its legacy tenant routes and V1 analytics remain available when workforce is selected.

Personal and organization accounts are application workspaces. Their membership is held and authorized by the application. Neither `tid`, an email address, nor an email domain grants workspace ownership. Team membership requires explicit application membership or a verified invitation. Enterprise SSO can later be configured as External ID federation without another application authentication scheme.

The API alone creates application users and personal workspaces. An external identity key is the validated issuer and the `sub` claim. The subject can be pairwise for an application; it must never be used to match users across applications or issuers. Email is descriptive data only and never an account merge key.

Trade-offs: a deployment cannot offer workforce and customer sign-in at the same time. Customer organization members use External ID sign-in until their federation is configured. The selected mode must be applied consistently to Web and API.

The customer authority uses `https://schwaburlshortener.ciamlogin.com/6adc36e9-dfe5-469f-8aed-6ef83abcdd19/v2.0` for the API bearer handler. Tenant discovery reports issuer `https://6adc36e9-dfe5-469f-8aed-6ef83abcdd19.ciamlogin.com/6adc36e9-dfe5-469f-8aed-6ef83abcdd19/v2.0`; issuer validation must follow discovery rather than reconstructing it from the vanity subdomain. Microsoft Identity Web accepts the customer `Instance` form `https://schwaburlshortener.ciamlogin.com/` with `TenantId`; the Web app's authority must resolve to the same tenant. See [Microsoft's Web API tutorial](https://learn.microsoft.com/en-us/entra/identity-platform/tutorial-web-api-dotnet-core-build-app) and [Blazor guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-entra?view=aspnetcore-10.0).
