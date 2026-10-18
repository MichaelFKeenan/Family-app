# One image holds the API, the built React app (ADR-012, ADR-034) and the hosted MCP server (ADR-041).

# 1. Build the React app.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi
COPY web/ ./
RUN npm run build

# 2. Publish the .NET host, and the MCP server into its own folder so their dependencies stay apart.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY tools/ tools/
RUN dotnet publish src/Host/FamilyApp.Host.csproj -c Release -o /app
RUN dotnet publish tools/FamilyApp.Mcp/FamilyApp.Mcp.csproj -c Release -o /app/mcp

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
# `dotnet FamilyApp.Host.dll migrate` before starting it (ADR-021, ADR-034), and Compose can run it
# as `dotnet mcp/FamilyApp.Mcp.dll --http` for the MCP server (ADR-041).
CMD ["dotnet", "FamilyApp.Host.dll"]
