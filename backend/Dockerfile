FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props Kanabol.sln ./
COPY src/Kanabol.Domain/*.csproj src/Kanabol.Domain/
COPY src/Kanabol.Application/*.csproj src/Kanabol.Application/
COPY src/Kanabol.Infrastructure/*.csproj src/Kanabol.Infrastructure/
COPY src/Kanabol.Api/*.csproj src/Kanabol.Api/
RUN dotnet restore src/Kanabol.Api/Kanabol.Api.csproj
COPY src/ src/
# The analyzers run in the commit hooks, with the rules in the root .editorconfig. That file is
# outside this build context, so here they would report rules the project has turned off.
RUN dotnet publish src/Kanabol.Api/Kanabol.Api.csproj -c Release -o /app --no-restore -p:RunAnalyzers=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# The image has neither wget nor curl, and the healthcheck needs one of them.
RUN apt-get update \
  && apt-get install -y --no-install-recommends wget \
  && rm -rf /var/lib/apt/lists/* \
  && mkdir -p /data/attachments \
  && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app .
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Kanabol.Api.dll"]
