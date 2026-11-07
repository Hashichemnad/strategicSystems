# syntax=docker/dockerfile:1
# Multi-stage build of the single deployable: ASP.NET Core host + built SPA. Runs as a non-root user.
# REGISTRY_PREFIX lets restricted networks pull Docker Hub images through a mirror (e.g. "mirror.gcr.io/").
# Behind a TLS-inspecting proxy, pass the organisation's CA bundle as the optional build secret "build_ca"
# (compose: BUILD_CA_FILE=/path/to/ca-bundle.pem). It is used only while downloading packages, never copied into an image.
ARG REGISTRY_PREFIX=

# Slow or unreliable networks (ADR 0163): npm, NuGet and apt retry with longer timeouts. The defaults only matter when a download fails;
# override them as build arguments (compose: the same names in deploy/.env).

FROM ${REGISTRY_PREFIX}library/node:22-alpine AS web
# npm: attempts per package, back-off between attempts (ms) and the time a single request may take (ms).
ARG NPM_FETCH_RETRIES=5
ARG NPM_FETCH_RETRY_MINTIMEOUT=20000
ARG NPM_FETCH_RETRY_MAXTIMEOUT=120000
ARG NPM_FETCH_TIMEOUT=600000
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN --mount=type=secret,id=build_ca,required=false \
    if [ -s /run/secrets/build_ca ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/build_ca; fi; \
    npm_config_fetch_retries="$NPM_FETCH_RETRIES" \
    npm_config_fetch_retry_mintimeout="$NPM_FETCH_RETRY_MINTIMEOUT" \
    npm_config_fetch_retry_maxtimeout="$NPM_FETCH_RETRY_MAXTIMEOUT" \
    npm_config_fetch_timeout="$NPM_FETCH_TIMEOUT" \
    npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
# Only the native libraries of the image's own architecture (linux-x64 or linux-arm64) are published: the multi-platform packages
# (SkiaSharp, PDFium…) otherwise ship Windows / macOS / musl binaries the container never loads. Framework-dependent (aspnet base image).
ARG TARGETARCH
# NuGet: its own per-request retries (count, delay in ms, also on HTTP 429), then whole restore attempts with a growing pause (packages
# already downloaded stay in the HTTP cache, so a later attempt continues). NuGet has no setting for its 100 s request timeout.
# NUGET_RESTORE_DISABLE_PARALLEL=true: fewer simultaneous connections on a very thin line.
ARG NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=10
ARG NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=3000
ARG NUGET_RESTORE_ATTEMPTS=3
ARG NUGET_RESTORE_DISABLE_PARALLEL=false
ENV NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=${NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT} \
    NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=${NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS} \
    NUGET_RETRY_HTTP_429=true
RUN --mount=type=secret,id=build_ca,required=false \
    if [ -s /run/secrets/build_ca ]; then export SSL_CERT_FILE=/run/secrets/build_ca; fi; \
    RID=linux-x64; if [ "$TARGETARCH" = "arm64" ]; then RID=linux-arm64; fi; \
    PARALLEL=; if [ "$NUGET_RESTORE_DISABLE_PARALLEL" = "true" ]; then PARALLEL=--disable-parallel; fi; \
    attempt=1; \
    until dotnet restore src/Host/Strategy.Host/Strategy.Host.csproj -r "$RID" $PARALLEL; do \
      if [ "$attempt" -ge "$NUGET_RESTORE_ATTEMPTS" ]; then echo "dotnet restore failed after $attempt attempts" >&2; exit 1; fi; \
      echo "dotnet restore failed (attempt $attempt of $NUGET_RESTORE_ATTEMPTS); retrying in $((attempt * 20)) s" >&2; \
      sleep $((attempt * 20)); attempt=$((attempt + 1)); \
    done
# The OpenAPI document is generated in the repository build, not inside the image.
# GIT_REVISION (optional) is stamped into the informational version ("1.0.0+<commit>") shown on the About and Health pages.
ARG GIT_REVISION=
RUN RID=linux-x64; if [ "$TARGETARCH" = "arm64" ]; then RID=linux-arm64; fi; \
    dotnet publish src/Host/Strategy.Host/Strategy.Host.csproj -c Release -o /app --no-restore -r "$RID" --self-contained false \
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
# to /app/licenses/ocr/ with a package list (name, version) and SOURCES.txt (Ubuntu source package and where to get it); the build fails if
# one is missing. libjbig0 is GPL-2+ (accepted by the owner, ADR 0033): NOTICE-libjbig0-GPL-2.txt and GPL-2.txt are added. docs/THIRD_PARTY_LICENSES.md lists them.
ARG INSTALL_OCR=true
# apt on slow networks (ADR 0163): attempts per download and request timeouts (s), passed as options (nothing stays in the image).
ARG APT_RETRIES=5
ARG APT_HTTP_TIMEOUT=120
# Security updates of the base image's OS packages first (Trivy gate: no High / Critical with a fix, ADR 0156).
RUN APT="apt-get -o Acquire::Retries=$APT_RETRIES -o Acquire::http::Timeout=$APT_HTTP_TIMEOUT -o Acquire::https::Timeout=$APT_HTTP_TIMEOUT" \
    && $APT update \
    && DEBIAN_FRONTEND=noninteractive $APT upgrade -y --no-install-recommends \
    && ($APT install -y --no-install-recommends libldap2 || $APT install -y --no-install-recommends libldap-2.5-0) \
    && if [ "$INSTALL_OCR" = "true" ]; then \
         dpkg-query -W -f='${Package}\n' | sort > /tmp/before \
         && $APT install -y --no-install-recommends tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng \
         && dpkg-query -W -f='${Package}\n' | sort > /tmp/after \
         && mkdir -p /app/licenses/ocr \
         && for p in $(comm -13 /tmp/before /tmp/after); do \
              test -f "/usr/share/doc/$p/copyright" || { echo "missing licence notice for $p"; exit 1; }; \
              cp "/usr/share/doc/$p/copyright" "/app/licenses/ocr/$p.copyright"; \
              dpkg-query -W -f='${Package} ${Version}\n' "$p" >> /app/licenses/ocr/packages.txt; \
              dpkg-query -W -f='${Package} ${Version} | source: ${source:Package} ${source:Version} | https://launchpad.net/ubuntu/+source/${source:Package}/${source:Version}\n' "$p" >> /app/licenses/ocr/SOURCES.txt; \
            done \
         && if grep -q '^libjbig0 ' /app/licenses/ocr/packages.txt; then \
              test -f /usr/share/common-licenses/GPL-2 || { echo "missing GPL-2 text for libjbig0"; exit 1; }; \
              cp /usr/share/common-licenses/GPL-2 /app/licenses/ocr/GPL-2.txt \
              && { printf '%s\n' \
                   "libjbig0 (JBIG-KIT) - GNU General Public License, version 2 or later (GPL-2+)" "" \
                   "This image contains the unmodified Ubuntu package libjbig0, a shared library loaded only by the separate tesseract program" \
                   "(through libtiff) for OCR (ADR 0033). It is licensed under the GPL, version 2 or later: full text in GPL-2.txt, copyright" \
                   "notice in libjbig0.copyright. Version and source package:"; \
                   dpkg-query -W -f='  ${Package} ${Version}, source package ${source:Package} ${source:Version}\n' libjbig0; \
                   printf '%s\n' "" \
                   "Corresponding source: https://launchpad.net/ubuntu/+source/$(dpkg-query -W -f='${source:Package}/${source:Version}' libjbig0)" \
                   "  or on an Ubuntu machine with source repositories: apt-get source $(dpkg-query -W -f='${source:Package}=${source:Version}' libjbig0)" \
                   "Images built with --build-arg INSTALL_OCR=false contain neither the OCR packages nor libjbig0."; \
                 } > /app/licenses/ocr/NOTICE-libjbig0-GPL-2.txt; \
            fi \
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
