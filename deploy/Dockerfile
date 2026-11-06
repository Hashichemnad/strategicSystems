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
# Tesseract OCR (Apache-2.0) with Arabic and English language data: text recognition of scanned documents, used only when switched on in
# Settings → AI (ADR 0033). Build with --build-arg INSTALL_OCR=false to leave it out (about 40 MB smaller; scans then cannot be read).
# Licence notices: the copyright file of every package the OCR install adds (Tesseract and the OS libraries it loads, some LGPL) is copied
# to /app/licenses/ocr/ with a package list (name, version); the build fails if one is missing. docs/THIRD_PARTY_LICENSES.md lists them.
ARG INSTALL_OCR=true
# apt-get upgrade: security fixes of the base image's packages published after the base image was built (Trivy, docs/security/scan-results.md).
RUN apt-get update \
    && apt-get upgrade -y --no-install-recommends \
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
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=1
COPY --from=build /app ./
COPY --from=web /src/web/dist ./wwwroot
# File store (local provider): a volume outside the web root, owned by the app user.
RUN mkdir -p /var/lib/strategy/files && chown -R $APP_UID /var/lib/strategy
ENV Files__LocalPath=/var/lib/strategy/files
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=6s --start-period=30s --retries=3 \
    CMD ["dotnet", "Strategy.Host.dll", "--health-check"]
ENTRYPOINT ["dotnet", "Strategy.Host.dll"]
