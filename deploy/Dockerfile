# syntax=docker/dockerfile:1
# Multi-stage build of the single deployable: ASP.NET Core host + built SPA. Runs as a non-root user.
# REGISTRY_PREFIX lets restricted networks pull Docker Hub images through a mirror (e.g. "mirror.gcr.io/").
# Behind a TLS-inspecting proxy, pass the organisation's CA bundle as the optional build secret "build_ca"
# (compose: BUILD_CA_FILE=/path/to/ca-bundle.pem). It is used only while downloading packages, never copied into an image.
ARG REGISTRY_PREFIX=

FROM ${REGISTRY_PREFIX}library/node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN --mount=type=secret,id=build_ca,required=false \
    if [ -s /run/secrets/build_ca ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/build_ca; fi; \
    npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN --mount=type=secret,id=build_ca,required=false \
    if [ -s /run/secrets/build_ca ]; then export SSL_CERT_FILE=/run/secrets/build_ca; fi; \
    dotnet restore src/Host/Strategy.Host/Strategy.Host.csproj
# The OpenAPI document is generated in the repository build, not inside the image.
RUN dotnet publish src/Host/Strategy.Host/Strategy.Host.csproj -c Release -o /app --no-restore \
    -p:OpenApiGenerateDocuments=false -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# libldap: LDAP / Active Directory sign-in (System.DirectoryServices.Protocols needs the native library on Linux).
RUN apt-get update \
    && (apt-get install -y --no-install-recommends libldap2 || apt-get install -y --no-install-recommends libldap-2.5-0) \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=1
COPY --from=build /app ./
COPY --from=web /src/web/dist ./wwwroot
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=6s --start-period=30s --retries=3 \
    CMD ["dotnet", "Strategy.Host.dll", "--health-check"]
ENTRYPOINT ["dotnet", "Strategy.Host.dll"]
