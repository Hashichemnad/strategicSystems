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
# GIT_REVISION (optional) is stamped into the informational version ("1.0.0+<commit>") shown on the About and Health pages.
ARG GIT_REVISION=
RUN dotnet publish src/Host/Strategy.Host/Strategy.Host.csproj -c Release -o /app --no-restore \
    -p:OpenApiGenerateDocuments=false -p:UseAppHost=false \
    ${GIT_REVISION:+-p:SourceRevisionId=$GIT_REVISION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# OCI image labels (ADR 0157): the install / upgrade scripts pass APP_VERSION, GIT_REVISION and BUILD_CREATED.
ARG APP_VERSION=1.0.0
ARG GIT_REVISION=
ARG BUILD_CREATED=
LABEL org.opencontainers.image.title="Integrated Strategy System" \
      org.opencontainers.image.description="Self-hosted, bilingual integrated strategy management suite (single deployable: host + SPA)" \
      org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.revision="${GIT_REVISION}" \
      org.opencontainers.image.created="${BUILD_CREATED}" \
      org.opencontainers.image.licenses="LicenseRef-Proprietary" \
      org.opencontainers.image.source="https://github.com/Hashichemnad/strategicSystems" \
      org.opencontainers.image.base.name="mcr.microsoft.com/dotnet/aspnet:10.0"
# libldap: LDAP / Active Directory sign-in (System.DirectoryServices.Protocols needs the native library on Linux).
# Tesseract OCR (Apache-2.0) with Arabic and English language data: text recognition of scanned documents, used only when switched on in
# Settings → AI (ADR 0033). Build with --build-arg INSTALL_OCR=false to leave it out (about 40 MB smaller; scans then cannot be read).
# Licence notices: the copyright file of every package the OCR install adds (Tesseract and the OS libraries it loads, some LGPL) is copied
# to /app/licenses/ocr/ with a package list (name, version); the build fails if one is missing. docs/THIRD_PARTY_LICENSES.md lists them.
ARG INSTALL_OCR=true
# Security updates of the base image's OS packages first (Trivy gate: no High / Critical with a fix, ADR 0156).
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get upgrade -y --no-install-recommends \
    && (apt-get install -y --no-install-recommends libldap2 || apt-get install -y --no-install-recommends libldap-2.5-0) \
    && if [ "$INSTALL_OCR" = "true" ]; then \
         dpkg-query -W -f='${Package}\n' | sort > /tmp/before \
         && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng \
         && dpkg-query -W -f='${Package}\n' | sort > /tmp/after \
         && mkdir -p /app/licenses/ocr \
         && for p in $(comm -13 /tmp/before /tmp/after); do \
              test -f "/usr/share/doc/$p/copyright" || { echo "missing licence notice for $p"; exit 1; }; \
              cp "/usr/share/doc/$p/copyright" "/app/licenses/ocr/$p.copyright"; \
              dpkg-query -W -f='${Package} ${Version}\n' "$p" >> /app/licenses/ocr/packages.txt; \
            done \
         && rm /tmp/before /tmp/after; \
       fi \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
# TMPDIR: the only writable path besides the files volume, so the container can run with a read-only root file system
# (compose: read_only + tmpfs /tmp). Data Protection keys live in the database, not in the home directory.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    TMPDIR=/tmp \
    ASPNETCORE_HTTP_PORTS= \
    APP_VERSION=${APP_VERSION}
COPY --from=build /app ./
COPY --from=web /src/web/dist ./wwwroot
# File store (local provider): a volume outside the web root, owned by the app user.
# /app stays owned by root (COPY default): the runtime user cannot change the application files.
RUN mkdir -p /var/lib/strategy/files && chown -R $APP_UID /var/lib/strategy
ENV Files__LocalPath=/var/lib/strategy/files
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=6s --start-period=30s --retries=3 \
    CMD ["dotnet", "Strategy.Host.dll", "--health-check"]
ENTRYPOINT ["dotnet", "Strategy.Host.dll"]
