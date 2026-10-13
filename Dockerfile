# One image holds the API and the built React app (ADR-012, ADR-034).

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
# Links the image in GitHub's registry to this repo, so the repo's workflows may pull it.
LABEL org.opencontainers.image.source="https://github.com/MichaelFKeenan/Family-app"
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
USER app
EXPOSE 8080
# CMD rather than ENTRYPOINT, so the deploy can run the same image as
# `dotnet FamilyApp.Host.dll migrate` before starting it (ADR-021, ADR-034).
CMD ["dotnet", "FamilyApp.Host.dll"]
