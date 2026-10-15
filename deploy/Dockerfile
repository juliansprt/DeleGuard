FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ ./src/
COPY policies/ ./policies/
COPY integrations/ide/ ./integrations/ide/
RUN dotnet restore src/Deleguard.Api/Deleguard.Api.csproj \
 && dotnet restore src/Deleguard.Workers/Deleguard.Workers.csproj
RUN dotnet publish src/Deleguard.Api/Deleguard.Api.csproj -c Release -o /app/api --no-restore
RUN dotnet publish src/Deleguard.Workers/Deleguard.Workers.csproj -c Release -o /app/worker --no-restore
COPY policies/ /app/policies/

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/api /app/api
COPY --from=build /app/worker /app/worker
COPY --from=build /app/policies /app/policies
COPY --from=build /app/policies /app/api/policies
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /app/keys
COPY deploy/entrypoint.sh /entrypoint.sh
RUN sed -i 's/\r$//' /entrypoint.sh && chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
