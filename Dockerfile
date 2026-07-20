# syntax=docker/dockerfile:1
# Single-image deploy: builds the React SPA + the .NET API that serves it.
# Build context = repo root.  Target host: any Docker host with a volume (see docker-compose.yml).

# ---------------------------------------------------------------------------
# 1) Build the React SPA (Vite) → outputs to backend/wwwroot/dist
# ---------------------------------------------------------------------------
FROM node:22-bookworm-slim AS frontend
WORKDIR /app/ClientApp
COPY ClientApp/package.json ClientApp/package-lock.json ./
RUN npm ci
COPY ClientApp/ ./
# Same-origin in production → axios uses relative /api (VITE_API_BASE_URL empty).
ENV VITE_API_BASE_URL=""
RUN npm run build

# ---------------------------------------------------------------------------
# 2) Restore + publish the .NET API (which also serves the built SPA)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/backend.csproj backend/
RUN dotnet restore backend/backend.csproj
COPY backend/ backend/
# Bring the compiled SPA into wwwroot/dist before publish so it ships in the image.
COPY --from=frontend /app/backend/wwwroot/dist backend/wwwroot/dist
RUN dotnet publish backend/backend.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------------------------------------------------------------------------
# 3) Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# DejaVu fonts → ImageSharp text overlay works headless (see ImageOverlayService FontCandidates).
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish ./
ENV ASPNETCORE_ENVIRONMENT=Production
# Kestrel binds to $PORT so the port can be overridden from .env without rebuilding.
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "dotnet backend.dll --urls http://0.0.0.0:${PORT:-8080}"]
