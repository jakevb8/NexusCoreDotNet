# ─── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /app

# Copy project file and restore dependencies first for better layer caching
COPY NexusCoreDotNet.csproj ./
RUN dotnet restore

# Copy source and publish
COPY . ./
RUN dotnet publish NexusCoreDotNet.csproj -c Release -o /app/publish --no-restore

# ─── Production stage ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runner

WORKDIR /app

COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "NexusCoreDotNet.dll"]
