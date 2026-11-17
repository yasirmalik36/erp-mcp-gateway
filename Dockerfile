# ---- 1. Angular Controller Console ----
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx ng build

# ---- 2. .NET gateway ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Directory.Build.props ErpMcpGateway.slnx ./
COPY src/ src/
RUN dotnet publish src/ErpMcp.Server -c Release -o /out --nologo
COPY --from=web /web/dist/console/browser /out/wwwroot

# ---- 3. Runtime (non-root, small) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out .
ENV ASPNETCORE_ENVIRONMENT=Demo \
    DOTNET_gcServer=0
USER $APP_UID
EXPOSE 8080
# Render and similar hosts pass PORT; default to 8080 locally.
CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet erp-mcp-gateway.dll"]
