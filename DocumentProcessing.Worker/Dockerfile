FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY DocumentProcessing.Domain/DocumentProcessing.Domain.csproj DocumentProcessing.Domain/
COPY DocumentProcessing.Application/DocumentProcessing.Application.csproj DocumentProcessing.Application/
COPY DocumentProcessing.Infrastructure/DocumentProcessing.Infrastructure.csproj DocumentProcessing.Infrastructure/
COPY DocumentProcessing.Worker/DocumentProcessing.Worker.csproj DocumentProcessing.Worker/
RUN dotnet restore DocumentProcessing.Worker/DocumentProcessing.Worker.csproj

COPY DocumentProcessing.Domain/ DocumentProcessing.Domain/
COPY DocumentProcessing.Application/ DocumentProcessing.Application/
COPY DocumentProcessing.Infrastructure/ DocumentProcessing.Infrastructure/
COPY DocumentProcessing.Worker/ DocumentProcessing.Worker/
RUN dotnet publish DocumentProcessing.Worker/DocumentProcessing.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
# krb5-libs: Npgsql probes for GSSAPI at startup and logs an error when libgssapi_krb5.so.2 is missing.
RUN apk add --no-cache krb5-libs
RUN mkdir -p /app/data/storage && chown -R app:app /app/data
USER app
ENTRYPOINT ["dotnet", "DocumentProcessing.Worker.dll"]
