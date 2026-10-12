# One image holds the API and the built React app (ADR-012, ADR-015).

# 1. Build the React app.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi
COPY web/ ./
RUN npm run build

# 2. Publish the .NET host.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Host/FamilyApp.Host.csproj -c Release -o /app

# 3. Runtime image: no SDK, no Node, non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
USER app
EXPOSE 8080
# CMD rather than ENTRYPOINT, so Fly's release command can replace it
# with `dotnet FamilyApp.Host.dll migrate` (ADR-021).
CMD ["dotnet", "FamilyApp.Host.dll"]
